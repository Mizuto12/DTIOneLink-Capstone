using DTIOneLink.Services;

namespace DTIOneLink.Tests;

// RecordsController talks to SQL Server directly (no EF, decision 8) and
// the test suite's own CustomWebApplicationFactory deliberately points
// that connection at an unreachable address so a raw-SQL controller "fails
// fast and safely instead of silently hitting a real database" — which is
// the actual reason no disposal tests existed before this. What's safely
// unit-testable without a database is the transition rules themselves, now
// extracted into RecordDisposalWorkflow — exactly where BUG-R1 lived
// (previously only RestoreToRetention checked its "from" status at all, so
// e.g. a Disposed record could be re-Archived and then restored to live
// Retention). Same pattern as TaskWorkflowTests.cs testing TaskWorkflow in
// isolation from TasksController.
public class RecordsDispositionTests
{
    [Theory]
    // The original four-state flow, still legal.
    [InlineData(RecordDisposalWorkflow.Retention, RecordDisposalWorkflow.Disposal, true)]
    [InlineData(RecordDisposalWorkflow.Retention, RecordDisposalWorkflow.Archived, true)]
    [InlineData(RecordDisposalWorkflow.Archived, RecordDisposalWorkflow.Retention, true)]
    // The new RA 9470/NAP pipeline, Disposal -> Disposed.
    [InlineData(RecordDisposalWorkflow.Disposal, RecordDisposalWorkflow.DisposalRequested, true)]
    [InlineData(RecordDisposalWorkflow.DisposalRequested, RecordDisposalWorkflow.AwaitingNapAuthority, true)]
    [InlineData(RecordDisposalWorkflow.AwaitingNapAuthority, RecordDisposalWorkflow.AuthorizedForDisposal, true)]
    [InlineData(RecordDisposalWorkflow.AuthorizedForDisposal, RecordDisposalWorkflow.Disposed, true)]
    // Archived is reachable as a branch off any pre-Disposed step, not just
    // Retention — a mis-click or a schedule correction partway through
    // requesting disposal authority isn't a dead end.
    [InlineData(RecordDisposalWorkflow.Disposal, RecordDisposalWorkflow.Archived, true)]
    [InlineData(RecordDisposalWorkflow.DisposalRequested, RecordDisposalWorkflow.Archived, true)]
    [InlineData(RecordDisposalWorkflow.AwaitingNapAuthority, RecordDisposalWorkflow.Archived, true)]
    [InlineData(RecordDisposalWorkflow.AuthorizedForDisposal, RecordDisposalWorkflow.Archived, true)]
    public void CanTransition_LegalEdges_AreAllowed(string from, string to, bool expected)
    {
        Assert.Equal(expected, RecordDisposalWorkflow.CanTransition(from, to));
    }

    [Theory]
    // BUG-R1, exactly: a Disposed record could be re-Archived, and an
    // Archived record could skip straight back to Disposed — breaking both
    // "Disposed is final" and "Archived/Disposed are mutually exclusive".
    [InlineData(RecordDisposalWorkflow.Disposed, RecordDisposalWorkflow.Archived)]
    [InlineData(RecordDisposalWorkflow.Archived, RecordDisposalWorkflow.Disposed)]
    // Disposed has no outgoing edges at all.
    [InlineData(RecordDisposalWorkflow.Disposed, RecordDisposalWorkflow.Retention)]
    [InlineData(RecordDisposalWorkflow.Disposed, RecordDisposalWorkflow.Disposal)]
    // Can't skip steps in the NAP pipeline.
    [InlineData(RecordDisposalWorkflow.Disposal, RecordDisposalWorkflow.AwaitingNapAuthority)]
    [InlineData(RecordDisposalWorkflow.Disposal, RecordDisposalWorkflow.AuthorizedForDisposal)]
    [InlineData(RecordDisposalWorkflow.Disposal, RecordDisposalWorkflow.Disposed)]
    [InlineData(RecordDisposalWorkflow.DisposalRequested, RecordDisposalWorkflow.AuthorizedForDisposal)]
    [InlineData(RecordDisposalWorkflow.DisposalRequested, RecordDisposalWorkflow.Disposed)]
    [InlineData(RecordDisposalWorkflow.AwaitingNapAuthority, RecordDisposalWorkflow.Disposed)]
    // RestoreToRetention is only legal from Archived — not from Disposal or
    // the NAP pipeline (there is no "restore" from mid-pipeline; Archived is
    // reached from there instead, which separately gets a way back).
    [InlineData(RecordDisposalWorkflow.Disposal, RecordDisposalWorkflow.Retention)]
    [InlineData(RecordDisposalWorkflow.DisposalRequested, RecordDisposalWorkflow.Retention)]
    [InlineData(RecordDisposalWorkflow.AwaitingNapAuthority, RecordDisposalWorkflow.Retention)]
    [InlineData(RecordDisposalWorkflow.AuthorizedForDisposal, RecordDisposalWorkflow.Retention)]
    // No self-transitions — this is also what makes a double-clicked action
    // a clean no-op/conflict instead of silently re-processing (BUG-R2):
    // the second click's "from" status no longer matches what the first
    // click already moved it to.
    [InlineData(RecordDisposalWorkflow.Retention, RecordDisposalWorkflow.Retention)]
    [InlineData(RecordDisposalWorkflow.Disposal, RecordDisposalWorkflow.Disposal)]
    [InlineData(RecordDisposalWorkflow.Disposed, RecordDisposalWorkflow.Disposed)]
    [InlineData(RecordDisposalWorkflow.Archived, RecordDisposalWorkflow.Archived)]
    public void CanTransition_IllegalEdges_AreRejected(string from, string to)
    {
        Assert.False(RecordDisposalWorkflow.CanTransition(from, to));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("SomeUnknownStatus")]
    public void CanTransition_UnknownOrBlankCurrentStatus_IsRejected(string? from)
    {
        Assert.False(RecordDisposalWorkflow.CanTransition(from, RecordDisposalWorkflow.Disposal));
    }

    [Fact]
    public void Disposed_HasNoAllowedNextStatuses()
    {
        Assert.Empty(RecordDisposalWorkflow.AllowedNextStatuses(RecordDisposalWorkflow.Disposed));
    }

    [Fact]
    public void Archived_OnlyAllowsRestoringToRetention()
    {
        Assert.Equal(new[] { RecordDisposalWorkflow.Retention }, RecordDisposalWorkflow.AllowedNextStatuses(RecordDisposalWorkflow.Archived));
    }

    [Fact]
    public void AllStatuses_ContainsEveryValueSetDisposalStatusCanWrite()
    {
        var expected = new[]
        {
            RecordDisposalWorkflow.Retention, RecordDisposalWorkflow.Disposal,
            RecordDisposalWorkflow.DisposalRequested, RecordDisposalWorkflow.AwaitingNapAuthority,
            RecordDisposalWorkflow.AuthorizedForDisposal, RecordDisposalWorkflow.Disposed,
            RecordDisposalWorkflow.Archived,
        };
        Assert.Equal(expected, RecordDisposalWorkflow.AllStatuses);
    }
}
