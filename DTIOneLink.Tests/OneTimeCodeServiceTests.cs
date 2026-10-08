using DTIOneLink.Models;
using DTIOneLink.Services;

namespace DTIOneLink.Tests;

// OneTimeCodeService.IssueAsync/VerifyAsync rely on EF Core's
// ExecuteUpdateAsync for their atomic concurrency guarantees (the
// "reserve an attempt in one statement" and "only the newest code works"
// rules) — EF Core's InMemory provider does not support ExecuteUpdateAsync
// ("could not be translated") and throws on every call, so those two
// methods can't be meaningfully unit tested without a real relational
// database. OneTimeCodeHasher, however, is pure HMAC logic with no DB
// dependency and is safe to test directly.
public class OneTimeCodeServiceTests
{
    [Fact]
    public void Hash_DifferentUserOrPurpose_ProducesDifferentHash()
    {
        // Binding user+purpose into the hash means a code can't be replayed
        // for another account or the other flow.
        var hasher = new OneTimeCodeHasher();
        var forUser1 = hasher.Hash(1, OneTimeCodePurpose.ConfirmEmail, "123456");
        var forUser2 = hasher.Hash(2, OneTimeCodePurpose.ConfirmEmail, "123456");
        var forOtherPurpose = hasher.Hash(1, OneTimeCodePurpose.PasswordReset, "123456");

        Assert.NotEqual(forUser1, forUser2);
        Assert.NotEqual(forUser1, forOtherPurpose);
    }

    [Fact]
    public void Hash_SameInputs_IsDeterministicWithinTheSameInstance()
    {
        // Same hasher instance (same in-memory key) must verify consistently,
        // since VerifyAsync re-hashes the entered code to compare.
        var hasher = new OneTimeCodeHasher();
        var first = hasher.Hash(1, OneTimeCodePurpose.ConfirmEmail, "123456");
        var second = hasher.Hash(1, OneTimeCodePurpose.ConfirmEmail, "123456");

        Assert.Equal(first, second);
    }

    [Fact]
    public void Hash_DifferentHasherInstances_ProduceDifferentHashes()
    {
        // Each hasher has its own random key created at app start — a code
        // issued before an app restart stops verifying after it, by design.
        var hasher1 = new OneTimeCodeHasher();
        var hasher2 = new OneTimeCodeHasher();

        Assert.NotEqual(
            hasher1.Hash(1, OneTimeCodePurpose.ConfirmEmail, "123456"),
            hasher2.Hash(1, OneTimeCodePurpose.ConfirmEmail, "123456"));
    }
}
