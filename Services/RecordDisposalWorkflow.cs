namespace DTIOneLink.Services
{
    // Single source of truth for Records disposal-status values and which
    // transitions are legal — same pattern as TaskWorkflow, for the same
    // reason. Previously RecordsController.SetDisposalStatus enforced a
    // "from" check for exactly one transition (RestoreToRetention) and
    // trusted the caller for every other one, so e.g. a Disposed record
    // could be re-Archived and then restored back to live Retention with
    // no gate stopping it. See docs/handoff/CHECKLIST.md "Records —
    // disposal & timeline".
    public static class RecordDisposalWorkflow
    {
        public const string Retention = "Retention";
        public const string Disposal = "Disposal";
        public const string DisposalRequested = "Disposal Requested";
        public const string AwaitingNapAuthority = "Awaiting NAP Authority";
        public const string AuthorizedForDisposal = "Authorized for Disposal";
        public const string Disposed = "Disposed";
        public const string Archived = "Archived";

        // Every value RecordsController.SetDisposalStatus can ever write —
        // also every value the Records.RecordStatus column may legally hold.
        public static readonly string[] AllStatuses =
        {
            Retention, Disposal, DisposalRequested, AwaitingNapAuthority,
            AuthorizedForDisposal, Disposed, Archived
        };

        // The NAP disposal pipeline, in order, once a record is flagged
        // Disposal: a Request for Authority to Dispose is submitted,
        // acknowledged as pending with NAP, then NAP authorizes it, and
        // only then is it actually (physically) disposed. Archived is a
        // branch off any step of this — it isn't a disposal outcome at
        // all, it's the alternative to one (RA 9470/NAP's permanent-value
        // records go to NAP for preservation, not destruction) — so a
        // mis-click or a schedule correction partway through requesting
        // disposal authority isn't a dead end. Disposed has no outgoing
        // edges: the system has no way to know whether reversing it would
        // be a lie about something that already physically happened —
        // same reasoning the original RestoreToRetention comment already
        // gave for why only Archived gets a way back.
        private static readonly Dictionary<string, string[]> AllowedTransitions = new()
        {
            [Retention] = new[] { Disposal, Archived },
            [Disposal] = new[] { DisposalRequested, Archived },
            [DisposalRequested] = new[] { AwaitingNapAuthority, Archived },
            [AwaitingNapAuthority] = new[] { AuthorizedForDisposal, Archived },
            [AuthorizedForDisposal] = new[] { Disposed, Archived },
            [Disposed] = Array.Empty<string>(),
            [Archived] = new[] { Retention },
        };

        public static bool CanTransition(string? currentStatus, string nextStatus)
        {
            if (string.IsNullOrWhiteSpace(currentStatus))
            {
                return false;
            }
            return AllowedTransitions.TryGetValue(currentStatus, out var allowed) && allowed.Contains(nextStatus);
        }

        public static string[] AllowedNextStatuses(string? currentStatus) =>
            !string.IsNullOrWhiteSpace(currentStatus) && AllowedTransitions.TryGetValue(currentStatus, out var allowed)
                ? allowed
                : Array.Empty<string>();
    }
}
