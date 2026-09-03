using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace DTIOneLink.Models
{
    public class ProofSubmissionViewModel
    {
        [Required]
        public int Id { get; set; }

        [Required(ErrorMessage = "Please attach a file as proof of completion.")]
        public IFormFile? ProofFile { get; set; }

        [Required(ErrorMessage = "Please add a remark describing what was completed.")]
        [StringLength(1000, ErrorMessage = "Remarks must be 1000 characters or fewer.")]
        public string Remarks { get; set; } = string.Empty;
    }
}