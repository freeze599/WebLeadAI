
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
            string cleanTitle = Normalize(title);
            string text = Normalize($"{title} {description}");

            var reasons = new List<string>();
            int score = 0;

            // Darba vakances, nevis ārštata pasūtījumi
            string[] jobIndicators =
            {
                "full-time employee",
                "permanent position",
                "annual salary",
                "employee benefits",
                "we are hiring",
                "join our team",
                "offre d'emploi",
                "recrutement cdi",
                "вакансія",
                "повна зайнятість",
                "постоянная работа в офисе"
            };

            if (HasAny(text, jobIndicators))
            {
                return Reject("Iespējama darba vakance");
            }

            // Pakalpojumu piedāvājumi, nevis pasūtījumi
            string[] offerIndicators =
            {
                "i offer website development",
                "i provide web development services",
                "hire me for web development",
                "my web development services",
                "je propose mes services",
                "je suis développeur freelance",
                "j'offre mes services",
                "предлагаю услуги разработки",
                "надаю послуги розробки"
            };

            if (HasAny(text, offerIndicators))
            {
                return Reject("Iespējams pakalpojumu piedāvājums");
            }

            // Galvenais: vai vispār ir mājaslapas projekts?
            string[] websiteKeywords =
            {
                "website",
                "web site",
                "webpage",
                "web page",
                "landing page",
                "web application",
                "web app",
                "e-commerce",
                "ecommerce",
                "online store",
                "site web",
                "site internet",
                "site vitrine",
                "boutique en ligne",
                "site e commerce",
                "site e-commerce",
                "mājaslapa",
                "mājaslapu",
                "tīmekļa vietne",
                "сайт",
                "сайту",
                "сайта",
                "вебсайт",
                "веб-сайт",
                "інтернет-магазин",
                "интернет-магазин"
            };

            bool hasWebsiteTopic = HasAny(text, websiteKeywords);

            // Konkrētas pasūtījuma darbības
            string[] buildKeywords =
            {
                "build a website",
                "create a website",
                "develop a website",
                "website development",
                "website creation",
                "website design",
                "need a website",
                "new website",
                "develop a web app",
                "build a web app",
                "création de site",
                "création d'un site",
                "creer un site",
                "créer un site",
                "développement de site",
                "developpement de site",
                "développeur site web",
                "developpeur site web",
                "izveidot mājaslapu",
                "mājaslapas izstrāde",
                "mājaslapu izstrāde",
                "создание сайта",
                "разработка сайта",
                "створення сайту",
                "розробка сайту"
            };

            string[] redesignKeywords =
            {
                "website redesign",
                "redesign website",
                "redesign my website",
                "website modernization",
                "rebuild website",
                "refonte de site",
                "refonte du site",
                "refonte d'un site",
                "refonte de mon site",
                "modernisation du site",
                "mājaslapas modernizācija",
                "mājaslapas pārveide",
                "редизайн сайта",
                "редизайн сайту",
                "обновление сайта",
                "оновлення сайту"
            };

            string[] requestKeywords =
            {
                "looking for a developer",
                "looking for someone",
                "looking for a freelancer",
                "need a developer",
                "need someone to",
                "i need",
                "we need",
                "i want to",
                "we want to",
                "seeking a developer",
                "looking to hire",
                "je cherche",
                "nous cherchons",
                "recherche développeur",
                "recherche developpeur",
                "besoin d'un développeur",
                "besoin d'un developpeur",
                "j'ai besoin",
                "nous avons besoin",
                "meklēju izstrādātāju",
                "nepieciešams izstrādātājs",
                "ищу разработчика",
                "нужен разработчик",
                "нужно сделать",
                "шукаю розробника",
                "потрібен розробник",
                "потрібно створити"
            };

            bool isBuild = HasAny(text, buildKeywords);
            bool isRedesign = HasAny(text, redesignKeywords);
            bool hasRequest = HasAny(text, requestKeywords);

            // Ar tehnoloģijas nosaukumu vien nepietiek
            if (!hasWebsiteTopic && !isBuild && !isRedesign)
            {
                return Reject(
                    "Nav pietiekamu mājaslapas projekta pazīmju");
            }

            if (isBuild)
            {
                score += 45;
                reasons.Add("Mājaslapas izstrādes projekts");
            }

            if (isRedesign)
            {
                score += 45;
                reasons.Add("Mājaslapas pārveides projekts");
            }

            if (!isBuild && !isRedesign && hasWebsiteTopic)
            {
                score += 15;
                reasons.Add("Pieminēts mājaslapas projekts");
            }

            if (hasRequest)
            {
                score += 20;
                reasons.Add("Ir pasūtītāja pieprasījuma pazīmes");
            }

            if (HasAny(text, new[]
            {
                "asp.net",
                "asp.net core",
                ".net core",
                "blazor",
                "c#"
            }))
            {
                score += 20;
                reasons.Add("Atbilst C# / ASP.NET prasmēm");
            }

            if (HasAny(text, new[]
            {
                "landing page",
                "portfolio website",
                "site vitrine",
                "site portfolio"
            }))
            {
                score += 10;
                reasons.Add("Landing page vai portfolio");
            }

            if (HasAny(text, new[]
            {
                "linux server",
                "nginx",
                "vps",
                "hosting setup",
                "server deployment"
            }))
            {
                score += 10;
                reasons.Add("Servera uzstādīšanas darbi");
            }

            if (HasAny(text, new[]
            {
                "wordpress",
                "woocommerce",
                "shopify",
                "prestashop"
            }))
            {
                score += 5;
                reasons.Add("CMS vai e-komercijas tehnoloģija");
            }

            // Virsraksts ir īpaši nozīmīgs
            if (HasAny(cleanTitle, buildKeywords) ||
                HasAny(cleanTitle, redesignKeywords))
            {
                score += 10;
                reasons.Add("Projekta mērķis skaidrs virsrakstā");
            }

            score = Math.Min(score, 100);

            // Vajadzīgs gan atbilstošs temats,
            // gan pietiekami skaidrs pasūtījums.
            bool isQualified =
                score >= 35 &&
                (isBuild || isRedesign ||
                 (hasWebsiteTopic && hasRequest));

            return new LeadQualificationResult
            {
                IsQualified = isQualified,
                Score = score,
                Reason = isQualified
                    ? string.Join("; ", reasons)
                    : "Nepietiekamu pasūtījuma pazīmju: " +
                      string.Join("; ", reasons)
            };
        }

        private static LeadQualificationResult Reject(
            string reason)
        {
            return new LeadQualificationResult
            {
                IsQualified = false,
                Score = 0,
                Reason = reason
            };
        }

        private static string Normalize(string? value)
        {
            string text = WebUtility.HtmlDecode(value ?? "");

            text = Regex.Replace(
                text,
                "<[^>]*>",
                " ");

            text = text.ToLowerInvariant();

            text = Regex.Replace(
                text,
                @"\s+",
                " ");

            return text.Trim();
        }

        private static bool HasAny(
            string text,
            IEnumerable<string> keywords)
        {
            return keywords.Any(keyword =>
                text.Contains(
                    keyword,
                    StringComparison.OrdinalIgnoreCase));
        }
    }

    public class LeadQualificationResult
    {
        public bool IsQualified { get; set; }
        public int Score { get; set; }
        public string Reason { get; set; } = "";
    }
}
