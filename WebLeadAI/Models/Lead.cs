
using System;
using System.ComponentModel.DataAnnotations;

namespace WebLeadAI.Models
{
    public class Lead
    {
        public int Id { get; set; }

        [Required, StringLength(200)]
        public string CompanyName { get; set; } = "";

        [StringLength(200)]
        public string? Website { get; set; }

        [EmailAddress, StringLength(254)]
        public string? Email { get; set; }

        [StringLength(500)]
        public string? SourceUrl { get; set; }

        [StringLength(100)]
        public string SourceType { get; set; } = "";

        public string? Description { get; set; }

        public int AILeadScore { get; set; } = 0;

        public string Status { get; set; } = "New";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? LastContactedAt { get; set; }

        [StringLength(20000)]
        public string? ProposalDraft { get; set; }

        [StringLength(2)]
        public string? ProposalLanguage { get; set; }

        public DateTime? ProposalSavedAt { get; set; }

        // IUB iepirkuma strukturētie dati. Null = nav droši zināms.
        public decimal? EstimatedBudget { get; set; }
        public DateTime? SubmissionDeadline { get; set; } // Latvijas vietējais laiks
        [StringLength(300)]
        public string? BuyerName { get; set; }

        // Kad sludinājums publicēts sākotnējā avotā.
        // Null nozīmē, ka datums nav zināms.
        public DateTime? PublishedAt { get; set; }
    }
}
