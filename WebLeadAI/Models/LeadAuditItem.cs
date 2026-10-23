
namespace WebLeadAI.Models
{
    public class LeadAuditItem
    {
        public int LeadId { get; set; }
        public string Title { get; set; } = "";

        public int OldScore { get; set; }
        public int NewScore { get; set; }
        public bool IsQualified { get; set; }
        public string Reason { get; set; } = "";
        public string CurrentStatus { get; set; } = "";

        public int Difference => NewScore - OldScore;

        // Jaunā algoritma 2.0 rezultāti
        public int OrderConfidence { get; set; }
        public int SkillMatch { get; set; }
        public int PriorityScore { get; set; }
        public bool IsQualifiedV2 { get; set; }
        public string OrderReason { get; set; } = "";
        public string SkillReason { get; set; } = "";
    }
}
