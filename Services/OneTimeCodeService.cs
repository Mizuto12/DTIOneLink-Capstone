using System.Security.Cryptography;
using System.Text;
using DTIOneLink.Data;
using DTIOneLink.Models;
using DTIOneLink.Security;
using DTIOneLink.Services.Email;
using Microsoft.EntityFrameworkCore;

namespace DTIOneLink.Services
{
    // Holds the secret key used to hash codes. It is random, created when the
    // app starts, and exists only in server memory — never in the database,
    // settings or source code. A 6-digit code has only 1,000,000 possible
    // values, so a plain hash could be reversed in seconds; with a secret key,
    // a stolen copy of the database reveals nothing. The cost: codes issued
    // before an app restart stop working, and the user asks for a new one
    // (they only last 10 minutes anyway).
    public class OneTimeCodeHasher
    {
        private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

        public string Hash(int userId, OneTimeCodePurpose purpose, string code)
        {
            // Binding the user and purpose means a code can't be replayed for
            // another account or the other flow.
            var bytes = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"{userId}:{(int)purpose}:{code}"));
            return Convert.ToHexString(bytes);
        }
    }

    public enum CodeIssueStatus { Sent, TooSoon, TooMany }

    public record CodeIssueResult(CodeIssueStatus Status, int WaitSeconds = 0);

    public enum CodeCheckStatus { Valid, Invalid, Expired, TooManyAttempts }

    public record CodeCheckResult(CodeCheckStatus Status, int AttemptsLeft = 0);

    public class OneTimeCodeService
    {
        private readonly AppDbContext _db;
        private readonly OneTimeCodeHasher _hasher;
        private readonly OtpEmailQueue _queue;
        private readonly ILogger<OneTimeCodeService> _logger;

        public OneTimeCodeService(AppDbContext db, OneTimeCodeHasher hasher, OtpEmailQueue queue, ILogger<OneTimeCodeService> logger)
        {
            _db = db;
            _hasher = hasher;
            _queue = queue;
            _logger = logger;
        }

        // Creates a new code (cancelling any earlier one for the same purpose)
        // and queues the email — unless the user asked too recently or too
        // often, which stops inbox flooding and collecting many codes to guess.
        public async Task<CodeIssueResult> IssueAsync(User user, OneTimeCodePurpose purpose)
        {
            var now = DateTime.UtcNow;
            var windowStart = now - AccountDefaults.CodeWindow;

            var recent = await _db.OneTimeCodes
                .Where(c => c.UserId == user.Id && c.Purpose == purpose && c.CreatedAtUtc >= windowStart)
                .Select(c => c.CreatedAtUtc)
                .OrderByDescending(t => t)
                .ToListAsync();

            if (recent.Count > 0 && now - recent[0] < AccountDefaults.CodeResendDelay)
            {
                var wait = AccountDefaults.CodeResendDelay - (now - recent[0]);
                return new CodeIssueResult(CodeIssueStatus.TooSoon, (int)Math.Ceiling(wait.TotalSeconds));
            }

            if (recent.Count >= AccountDefaults.MaxCodesPerWindow)
            {
                var wait = recent[AccountDefaults.MaxCodesPerWindow - 1] + AccountDefaults.CodeWindow - now;
                return new CodeIssueResult(CodeIssueStatus.TooMany, (int)Math.Ceiling(wait.TotalSeconds));
            }

            // Only the newest code works.
            await _db.OneTimeCodes
                .Where(c => c.UserId == user.Id && c.Purpose == purpose && c.ConsumedAtUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAtUtc, now));

            // Cryptographically secure, evenly spread over 000000–999999.
            var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

            _db.OneTimeCodes.Add(new OneTimeCode
            {
                UserId = user.Id,
                Purpose = purpose,
                CodeHash = _hasher.Hash(user.Id, purpose, code),
                CreatedAtUtc = now,
                ExpiresAtUtc = now + AccountDefaults.CodeLifetime
            });
            await _db.SaveChangesAsync();

            var message = EmailTemplates.Code(user.Email, user.FullName, purpose, code, (int)AccountDefaults.CodeLifetime.TotalMinutes);
            if (!_queue.TryEnqueue(message))
            {
                _logger.LogError("The verification email queue is full; a code for user {UserId} was not sent.", user.Id);
            }

            _logger.LogInformation("Issued a {Purpose} code for user {UserId}.", purpose, user.Id);
            return new CodeIssueResult(CodeIssueStatus.Sent);
        }

        // Checks a typed code against the newest unused code. Each check first
        // reserves one of the code's attempts in a single database statement
        // that only succeeds while attempts remain — so even many requests
        // sent at the same instant get at most MaxCodeAttempts comparisons.
        // A correct code is then marked used the same way, so two
        // simultaneous submissions can't both succeed.
        public async Task<CodeCheckResult> VerifyAsync(int userId, OneTimeCodePurpose purpose, string? enteredCode)
        {
            var now = DateTime.UtcNow;
            var current = await _db.OneTimeCodes.AsNoTracking()
                .Where(c => c.UserId == userId && c.Purpose == purpose && c.ConsumedAtUtc == null)
                .OrderByDescending(c => c.CreatedAtUtc)
                .FirstOrDefaultAsync();

            if (current == null || current.ExpiresAtUtc <= now)
            {
                return new CodeCheckResult(CodeCheckStatus.Expired);
            }

            var reserved = await _db.OneTimeCodes
                .Where(c => c.Id == current.Id && c.ConsumedAtUtc == null && c.FailedAttempts < AccountDefaults.MaxCodeAttempts)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.FailedAttempts, c => c.FailedAttempts + 1));
            if (reserved == 0)
            {
                return new CodeCheckResult(CodeCheckStatus.TooManyAttempts);
            }

            var code = new string((enteredCode ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
            var matches = code.Length == 6 && CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(_hasher.Hash(userId, purpose, code)),
                Encoding.ASCII.GetBytes(current.CodeHash));

            if (!matches)
            {
                var used = await _db.OneTimeCodes.Where(c => c.Id == current.Id).Select(c => c.FailedAttempts).FirstAsync();
                var left = AccountDefaults.MaxCodeAttempts - used;
                _logger.LogWarning("Wrong {Purpose} code entered for user {UserId}.", purpose, userId);
                return left > 0
                    ? new CodeCheckResult(CodeCheckStatus.Invalid, left)
                    : new CodeCheckResult(CodeCheckStatus.TooManyAttempts);
            }

            var claimed = await _db.OneTimeCodes
                .Where(c => c.Id == current.Id && c.ConsumedAtUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAtUtc, now));

            return claimed == 1
                ? new CodeCheckResult(CodeCheckStatus.Valid)
                : new CodeCheckResult(CodeCheckStatus.Expired);
        }
    }
}
