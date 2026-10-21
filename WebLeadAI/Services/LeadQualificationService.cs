
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

            text = Regex.Replace(
                text.ToLowerInvariant(),
                @"\s+", " ");

            int score = 0;
            var reasons = new List<string>();

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
                "offre d'emploi",
                "вакансія",
                "повна зайнятість"
            };

            if (jobIndicators.Any(text.Contains))
            {
                return new LeadQualificationResult
                {
                    IsQualified = false,
                    Score = 0,
                    Reason = "Iespējama darba vakance"
                };
            }

            AddScore(
                new[]
                {
                    "website development",
                    "build a website",
                    "create a website",
                    "need a website",
                    "mājaslapas izstrāde",
                    "izveidot mājaslapu",
                    "création de site",
                    "création d'un site",
                    "створення сайту",
                    "розробка сайту",
                    "разработка сайта",
                    "создание сайта"
                },
                40,
                "Jaunas mājaslapas izstrāde");

            AddScore(
                new[]
                {
                    "website redesign",
                    "website modernization",
                    "refonte de site",
                    "redesign website",
                    "mājaslapas modernizācija",
                    "редизайн сайта",
                    "редизайн сайту"
                },
                35,
                "Esošās mājaslapas modernizācija");

            AddScore(
                new[]
                {
                    "asp.net",
                    ".net core",
                    "c#",
                    "blazor"
                },
                25,
                "Atbilst ASP.NET / C# prasmēm");

            AddScore(
                new[]
                {
                    "landing page",
                    "portfolio website",
                    "site vitrine",
                    "site portfolio",
                    "site internet",
                    "site web",
                    "вебсайт",
                    "mājaslapa"
                },
                20,
                "Mājaslapas vai landing page projekts");

            AddScore(
                new[]
                {
                    "linux server",
                    "nginx",
                    "vps",
                    "deployment",
                    "hosting setup"
                },
                15,
                "Servera uzstādīšanas darbi");

            AddScore(
                new[]
                {
                    "wordpress",
                    "woocommerce",
                    "shopify",
                    "prestashop"
                },
                10,
                "CMS vai e-komercijas projekts");

            score = Math.Min(score, 100);

            return new LeadQualificationResult
            {
                IsQualified = score >= 20,
                Score = score,
                Reason = reasons.Count > 0
                    ? string.Join("; ", reasons)
                    : "Nav atrastas atbilstības pazīmes"
            };

            void AddScore(
                string[] keywords,
                int points,
                string reason)
            {
                if (keywords.Any(text.Contains))
                {
                    score += points;
                    reasons.Add(reason);
                }
            }
        }
    }

    public class LeadQualificationResult
    {
        public bool IsQualified { get; set; }
        public int Score { get; set; }
        public string Reason { get; set; } = "";
    }
}
