using DTIOneLink.Services;

namespace DTIOneLink.Tests;

// RecordsController talks to SQL Server directly (no EF, decision 8), and
// the test suite's own CustomWebApplicationFactory deliberately points
// that connection at an unreachable address so a raw-SQL controller fails
// fast instead of silently hitting a real database — the same reason
// RecordsDispositionTests.cs tests RecordDisposalWorkflow directly instead
// of RecordsController. The scoping rule for attachment upload/delete
// (RecordAttachmentAccess.CanManage) is the part of "attachment retrieval
// scoping" that's actually unit-testable without a live SQL Server: a
// records manager, or the record's own creator, same rule CanEditRow and
// CanDisposeRow already use (this is their shared implementation).
//
// Read access (DownloadAttachment, FileBrowser) reuses VisibilityClause's
// own SQL fragment by direct call, not a re-implementation — so it cannot
// drift from the table view's own visibility by construction, and has no
// separate logic of its own to unit-test.
public class RecordAttachmentAccessTests
{
    [Fact]
    public void CanManage_RecordsManager_AlwaysAllowed_EvenOnSomeoneElsesRecord()
    {
        Assert.True(RecordAttachmentAccess.CanManage(userCanManage: true, recordCreatedByUserId: 999, userId: 1));
    }

    [Fact]
    public void CanManage_RecordsOwnCreator_Allowed()
    {
        Assert.True(RecordAttachmentAccess.CanManage(userCanManage: false, recordCreatedByUserId: 1, userId: 1));
    }

    [Fact]
    public void CanManage_NeitherManagerNorCreator_Denied()
    {
        Assert.False(RecordAttachmentAccess.CanManage(userCanManage: false, recordCreatedByUserId: 999, userId: 1));
    }

    [Fact]
    public void CanManage_LegacyRecordWithNoStoredOwner_DeniedUnlessManager()
    {
        // A record logged before CreatedByUserId existed has a null owner —
        // nobody but a manager can touch its attachments, same as
        // VisibilityClause already treats it as invisible to anyone else.
        Assert.False(RecordAttachmentAccess.CanManage(userCanManage: false, recordCreatedByUserId: null, userId: 1));
        Assert.True(RecordAttachmentAccess.CanManage(userCanManage: true, recordCreatedByUserId: null, userId: 1));
    }
}
