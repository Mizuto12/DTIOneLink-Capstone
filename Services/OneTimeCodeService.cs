using System.Security.Cryptography;
using System.Text;
using DTIOneLink.Data;
using DTIOneLink.Models;
using DTIOneLink.Security;
using DTIOneLink.Services.Email;
using Microsoft.AspNetCore.WebUtilities;
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

        // Shared by IssueAsync and IssueLinkAsync: how many of this purpose
        // were issued recently, so both stop inbox flooding and collecting
        // many codes/links to guess the same way.
        private async Task<CodeIssueResult?> CheckThrottleAsync(int userId, OneTimeCodePurpose purpose, DateTime now)
        {
            var windowStart = now - AccountDefaults.CodeWindow;

            var recent = await _db.OneTimeCodes
                .Where(c => c.UserId == userId && c.Purpose == purpose && c.CreatedAtUtc >= windowStart)
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

            return null;
        }

        // Cancels every unconsumed code/link of this purpose without issuing
        // a replacement — for when some other action makes the outstanding
        // one moot (e.g. an admin hands out a temporary password for an
        // account whose activation link never arrived: only one "become
        // this account" credential should ever be live at a time).
        public async Task InvalidateAsync(int userId, OneTimeCodePurpose purpose)
        {
            await _db.OneTimeCodes
                .Where(c => c.UserId == userId && c.Purpose == purpose && c.ConsumedAtUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAtUtc, DateTime.UtcNow));
        }

        // Creates a new code (cancelling any earlier one for the same purpose)
        // and queues the email — unless the user asked too recently or too
        // often, which stops inbox flooding and collecting many codes to guess.
        public async Task<CodeIssueResult> IssueAsync(User user, OneTimeCodePurpose purpose)
        {
            var now = DateTime.UtcNow;
            if (await CheckThrottleAsync(user.Id, purpose, now) is { } throttled)
            {
                return throttled;
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

        // Password reset's equivalent of IssueAsync, except it emails a
        // clickable link instead of a 6-digit code: a 256-bit random token
        // instead of one of only a million 6-digit values, so there is no
        // meaningful "guess it" attack surface the way there is for a typed
        // code — the throttling here is purely against inbox flooding, same
        // as IssueAsync. buildLink turns the raw token into the actual URL;
        // only a controller action has the Url/Request context to build
        // that, so it's supplied by the caller rather than built in here.
        public async Task<CodeIssueResult> IssueLinkAsync(User user, OneTimeCodePurpose purpose, Func<string, string> buildLink)
        {
            var now = DateTime.UtcNow;
            if (await CheckThrottleAsync(user.Id, purpose, now) is { } throttled)
            {
                return throttled;
            }

            await _db.OneTimeCodes
                .Where(c => c.UserId == user.Id && c.Purpose == purpose && c.ConsumedAtUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAtUtc, now));

            var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            var lifetime = purpose == OneTimeCodePurpose.AccountActivation
                ? AccountDefaults.ActivationLinkLifetime
                : AccountDefaults.ResetLinkLifetime;

            _db.OneTimeCodes.Add(new OneTimeCode
            {
                UserId = user.Id,
                Purpose = purpose,
                CodeHash = _hasher.Hash(user.Id, purpose, token),
                CreatedAtUtc = now,
                ExpiresAtUtc = now + lifetime
            });
            await _db.SaveChangesAsync();

            var message = purpose == OneTimeCodePurpose.AccountActivation
                ? EmailTemplates.ActivationLink(user.Email, user.FullName, buildLink(token), (int)lifetime.TotalHours)
                : EmailTemplates.ResetLink(user.Email, user.FullName, buildLink(token), (int)lifetime.TotalMinutes);
            if (!_queue.TryEnqueue(message))
            {
                _logger.LogError("The verification email queue is full; a {Purpose} link for user {UserId} was not sent.", purpose, user.Id);
            }

            _logger.LogInformation("Issued a {Purpose} link for user {UserId}.", purpose, user.Id);
            return new CodeIssueResult(CodeIssueStatus.Sent);
        }

        // Read-only check for a link token: same matching rules as
        // VerifyLinkAsync, but never consumes it. Callers use this for the
        // page a GET request first lands on, specifically because some
        // email security gateways and link-preview features automatically
        // visit a link to scan it before a human ever clicks it — if GET
        // consumed the token the way it used to, that automated visit would
        // burn a genuine one-time link before its owner saw it. The actual
        // consuming check (VerifyLinkAsync) only ever runs from an explicit
        // POST the person submits themselves.
        public async Task<CodeCheckResult> PeekLinkAsync(int userId, OneTimeCodePurpose purpose, string? token)
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

            var matches = !string.IsNullOrEmpty(token) && CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(_hasher.Hash(userId, purpose, token)),
                Encoding.ASCII.GetBytes(current.CodeHash));

            if (!matches)
            {
                _logger.LogWarning("Wrong/forged {Purpose} link token previewed for user {UserId}.", purpose, userId);
                return new CodeCheckResult(CodeCheckStatus.Invalid);
            }

            return new CodeCheckResult(CodeCheckStatus.Valid);
        }

        // Actually consumes a link token — only reachable from the POST a
        // person submits after PeekLinkAsync showed them a "click to
        // continue" confirmation page. No attempt-limiting, because a
        // 256-bit token isn't something worth rate-limiting guesses against
        // the way a 6-digit code is — brute-forcing it is computationally
        // infeasible regardless. The caller still wraps this action in the
        // normal rate limiter against sheer request volume.
        public async Task<CodeCheckResult> VerifyLinkAsync(int userId, OneTimeCodePurpose purpose, string? token)
        {
            var peeked = await PeekLinkAsync(userId, purpose, token);
            if (peeked.Status != CodeCheckStatus.Valid)
            {
                return peeked;
            }

            var now = DateTime.UtcNow;
            // PeekLinkAsync already confirmed a live, matching code exists —
            // re-select it (AsNoTracking there gave no entity to update
            // directly) and consume it the same atomic way VerifyAsync
            // consumes a typed code: a single conditional UPDATE, so two
            // concurrent requests for the same token can't both succeed.
            var current = await _db.OneTimeCodes.AsNoTracking()
                .Where(c => c.UserId == userId && c.Purpose == purpose && c.ConsumedAtUtc == null)
                .OrderByDescending(c => c.CreatedAtUtc)
                .FirstOrDefaultAsync();
            if (current == null)
            {
                return new CodeCheckResult(CodeCheckStatus.Expired);
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
