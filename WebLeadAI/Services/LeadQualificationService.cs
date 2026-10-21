
using System.Text.RegularExpressions;

namespace WebLeadAI.Services
{
    public class LeadQualificationService
    {

        public LeadQualificationResult Evaluate(
            string title,
            string description)
        {
            string text = Regex.Replace(
                $"{title} {description}".ToLowerInvariant(),
                "<[^>]*>", " ");

            string[] jobIndicators =
            {
                "full-time",
                "full time",
                "permanent position",
                "annual salary",
                "salary range",
                "employee benefits",
                "staff engineer",
                "senior software engineer",
                "engineering manager",
                "apply for this job",
                "we are hiring",
                "join our team"
            };

            string[] projectIndicators =
            {
                "need a website",
                "website redesign",
                "website development project",
                "looking for a web developer",
                "website modernization",
                "freelance web developer",
                "contract web developer",
                "izveidot mājaslapu",
                "mājaslapas izstrāde",
                "meklējam mājaslapas izstrādātāju",
                "mājaslapas modernizācija",
                "разработка сайта",
                "создание сайта",
                "нужен сайт",
                "редизайн сайта"
            };

            bool jobMatch = jobIndicators.Any(
                x => text.Contains(x));

            bool projectMatch = projectIndicators.Any(
                x => text.Contains(x));

            // Darba vakances pazīmes ir spēcīgs
            // iemesls neuzskatīt ierakstu par klientu.
            if (jobMatch)
            {
                return new LeadQualificationResult
                {
                    IsQualified = false,
                    Score = 0,
                    Reason = "Iespējama darba vakance"
                };
            }

            if (projectMatch)
            {
                return new LeadQualificationResult
                {
                    IsQualified = true,
                    Score = 70,
                    Reason = "Iespējams mājaslapas izstrādes pasūtījums"
                };
            }

            return new LeadQualificationResult
            {
                IsQualified = false,
                Score = 0,
                Reason = "Nav pietiekamu pasūtījuma pazīmju"
            };
        }
    }


    public class LeadQualificationResult
    {
        public bool IsQualified { get; set; }

        public int Score { get; set; }

        public string Reason { get; set; } = "";
    }
}
