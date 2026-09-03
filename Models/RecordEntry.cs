namespace DTIOneLink.Models
{
    public class RecordEntry
    {
        public int RecordId { get; set; }
        public required string Code { get; set; }
        public required string Title { get; set; }
        public required string Medium { get; set; }
        public required string Location { get; set; }
        public required string PeriodCovered { get; set; }
        public required string FilingSystem { get; set; }
        public required string AccessControl { get; set; }
        public required string RetentionPeriod { get; set; }
    }
}