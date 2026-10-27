namespace DTIOneLink.Models
{
    // One row in the "digital locker" file browser (RecordsController.FileBrowser).
    public class RecordAttachmentViewModel
    {
        public int AttachmentId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public DateTime UploadedAt { get; set; }
        public string? UploadedByName { get; set; }

        public int RecordId { get; set; }
        public string RecordCode { get; set; } = string.Empty;
        public string RecordTitle { get; set; } = string.Empty;
        public string AccessControl { get; set; } = string.Empty;

        // Visual cue only (decision: no new permission logic) — Confidential
        // records show a lock icon; every other AccessControl value doesn't.
        public bool IsLocked => string.Equals(AccessControl, "Confidential", StringComparison.OrdinalIgnoreCase);
    }
}
