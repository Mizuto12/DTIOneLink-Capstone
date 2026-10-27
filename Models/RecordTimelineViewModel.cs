namespace DTIOneLink.Models
{
    public class RecordDispositionEventViewModel
    {
        public string EventType { get; set; } = string.Empty;
        public DateTime OccurredAt { get; set; }
        public string? AuthorityReference { get; set; }
        public string? Notes { get; set; }
        public string? ActorName { get; set; }
    }

    public class RecordTimelineViewModel
    {
        public int RecordId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string CurrentStatus { get; set; } = string.Empty;
        public List<RecordDispositionEventViewModel> Events { get; set; } = new();
    }
}
