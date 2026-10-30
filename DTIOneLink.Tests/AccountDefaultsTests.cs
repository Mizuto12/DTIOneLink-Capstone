using DTIOneLink.Security;

namespace DTIOneLink.Tests;

// Regression coverage for removing AccountDefaults.DefaultPassword
// (2026-10-30): new accounts never had a real default password anyway
// (UserManagementController.Create already used an unguessable random
// value + an activation link before this change) — the constant only
// remained as a legacy login-detection branch and a banned-password
// entry, neither of which any live code path could still reach. This
// test locks in the one externally-visible guarantee: the old default
// string stays banned as a new password even without the named constant.
public class AccountDefaultsTests
{
    [Theory]
    [InlineData("dtionelink2026")]
    [InlineData("DTIONELINK2026")] // CommonPasswords is case-insensitive
    public void CommonPasswords_StillBansTheOldDefaultPassword(string value)
    {
        Assert.Contains(value, AccountDefaults.CommonPasswords);
    }
}
