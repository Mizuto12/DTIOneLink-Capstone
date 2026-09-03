namespace DTIOneLink.Services
{
    public static class ProofFileValidator
    {
        public const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB

        private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".docx", ".xlsx", ".jpg", ".jpeg", ".png"
        };

        // First few bytes of each allowed file type. DOCX/XLSX are both
        // ZIP containers under the hood, so they share a signature —
        // the extension check above is what actually separates them.
        private static readonly Dictionary<string, byte[][]> Signatures = new(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"]  = new[] { new byte[] { 0x25, 0x50, 0x44, 0x46 } },             // %PDF
            [".docx"] = new[] { new byte[] { 0x50, 0x4B, 0x03, 0x04 } },             // PK.. (zip)
            [".xlsx"] = new[] { new byte[] { 0x50, 0x4B, 0x03, 0x04 } },             // PK.. (zip)
            [".jpg"]  = new[] { new byte[] { 0xFF, 0xD8, 0xFF } },
            [".jpeg"] = new[] { new byte[] { 0xFF, 0xD8, 0xFF } },
            [".png"]  = new[] { new byte[] { 0x89, 0x50, 0x4E, 0x47 } },
        };

        public static (bool IsValid, string? Error) Validate(IFormFile file)
        {
            if (file.Length == 0)
                return (false, "The uploaded file is empty.");

            if (file.Length > MaxFileSizeBytes)
                return (false, "File must be 10 MB or smaller.");

            var ext = Path.GetExtension(file.FileName);
            if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
                return (false, "Only PDF, DOCX, XLSX, JPG, and PNG files are allowed.");

            using var stream = file.OpenReadStream();
            var header = new byte[8];
            var bytesRead = stream.Read(header, 0, header.Length);
            stream.Seek(0, SeekOrigin.Begin); // reset so the caller can still read the full file

            if (bytesRead < 4 || !Signatures.TryGetValue(ext, out var validSignatures))
                return (false, "Could not verify file type.");

            var matchesAnySignature = validSignatures.Any(sig =>
                header.Take(sig.Length).SequenceEqual(sig));

            if (!matchesAnySignature)
                return (false, "The file's contents don't match its extension. Please re-upload a genuine file of that type.");

            return (true, null);
        }
    }
}