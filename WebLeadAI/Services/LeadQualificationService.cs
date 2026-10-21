
using System.Net;
using System.Text.RegularExpressions;

namespace WebLeadAI.Services
{
    public class LeadQualificationService
    {
        public LeadQualificationResult Evaluate(
            string title,
            string description)
        {
            string text = WebUtility.HtmlDecode(
                $"{title} {description}");

            text = Regex.Replace(
                text, "<[^>]*>", " ");

            text = text.ToLowerInvariant();

            string[] jobIndicators =
            {
                "full-time employee",
                "permanent position",
                "annual salary",
                "employee benefits",
                "staff engineer",
                "engineering manager",
                "we are hiring",
                "join our team",
                "poste permanent",
                "offre d'emploi",
                "вакансія",
                "повна зайнятість"
            };

            string[] projectIndicators =
            {
                "website redesign",
                "website development",
                "build a website",
                "create a website",
                "need a website",
                "freelance web developer",
                "website modernization",
                "landing page",
                "e-commerce website",
                "mājaslapas izstrāde",
                "izveidot mājaslapu",
                "mājaslapas modernizācija",
                "vajadzīga mājaslapa",
                "разработка сайта",
                "создание сайта",
                "редизайн сайта",
                "création de site",
                "création d'un site",
                "refonte de site",
                "site internet",
                "site web",
                "développement web",
                "створення сайту",
                "розробка сайту",
                "редизайн сайту",
                "вебсайт",
                "інтернет-магазин"
            };

            bool isJob = jobIndicators.Any(
                x => text.Contains(x));

            bool isProject = projectIndicators.Any(
                x => text.Contains(x));

            if (isJob)
            {
                return new LeadQualificationResult
                {
                    IsQualified = false,
                    Score = 0,
                    Reason = "Iespējama darba vakance"
                };
            }

            if (isProject)
            {
                return new LeadQualificationResult
                {
                    IsQualified = true,
                    Score = 70,
                    Reason = "Iespējams mājaslapas pasūtījums"
                };
            }

            return new LeadQualificationResult
            {
                IsQualified = false,
                Score = 0,
                Reason = "Nav pietiekamu atbilstības pazīmju"
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
