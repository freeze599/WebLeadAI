
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace WebLeadAI.Services
{
    public class LeadQualificationService
    {
        // ESOŠAIS ALGORITMS — saglabāts savietojamībai.
        public LeadQualificationResult Evaluate(
            string title,
            string description)
        {
            string cleanTitle = Normalize(title);
            string text = Normalize($"{title} {description}");

            var reasons = new List<string>();
            int score = 0;

            string[] jobIndicators =
            {
                "full-time employee", "permanent position",
                "annual salary", "employee benefits",
                "we are hiring", "join our team",
                "offre d'emploi", "recrutement cdi",
                "вакансія", "повна зайнятість",
                "постоянная работа в офисе"
            };

            if (HasAny(text, jobIndicators))
                return Reject("Iespējama darba vakance");

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
                return Reject("Iespējams pakalpojumu piedāvājums");

            string[] websiteKeywords =
            {
                "website", "web site", "webpage", "web page",
                "landing page", "web application", "web app",
                "e-commerce", "ecommerce", "online store",
                "site web", "site internet", "site vitrine",
                "boutique en ligne", "site e commerce",
                "site e-commerce", "mājaslapa", "mājaslapu",
                "tīmekļa vietne", "сайт", "сайту", "сайта",
                "вебсайт", "веб-сайт", "інтернет-магазин",
                "интернет-магазин"
            };

            string[] buildKeywords =
            {
                "build a website", "create a website",
                "develop a website", "website development",
                "website creation", "website design",
                "need a website", "new website",
                "develop a web app", "build a web app",
                "création de site", "création d'un site",
                "creer un site", "créer un site",
                "développement de site",
                "developpement de site",
                "développeur site web",
                "developpeur site web",
                "izveidot mājaslapu",
                "mājaslapas izstrāde",
                "mājaslapu izstrāde",
                "создание сайта", "разработка сайта",
                "створення сайту", "розробка сайту"
            };

            string[] redesignKeywords =
            {
                "website redesign", "redesign website",
                "redesign my website",
                "website modernization", "rebuild website",
                "refonte de site", "refonte du site",
                "refonte d'un site", "refonte de mon site",
                "modernisation du site",
                "mājaslapas modernizācija",
                "mājaslapas pārveide",
                "редизайн сайта", "редизайн сайту",
                "обновление сайта", "оновлення сайту"
            };

            string[] requestKeywords =
            {
                "looking for a developer",
                "looking for someone",
                "looking for a freelancer",
                "need a developer", "need someone to",
                "i need", "we need", "i want to",
                "we want to", "seeking a developer",
                "looking to hire", "je cherche",
                "nous cherchons",
                "recherche développeur",
                "recherche developpeur",
                "besoin d'un développeur",
                "besoin d'un developpeur",
                "j'ai besoin", "nous avons besoin",
                "meklēju izstrādātāju",
                "nepieciešams izstrādātājs",
                "ищу разработчика",
                "нужен разработчик", "нужно сделать",
                "шукаю розробника",
                "потрібен розробник",
                "потрібно створити"
            };

            bool hasWebsite = HasAny(text, websiteKeywords);
            bool isBuild = HasAny(text, buildKeywords);
            bool isRedesign = HasAny(text, redesignKeywords);
            bool hasRequest = HasAny(text, requestKeywords);

            if (!hasWebsite && !isBuild && !isRedesign)
                return Reject(
                    "Nav pietiekamu mājaslapas projekta pazīmju");

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

            if (!isBuild && !isRedesign && hasWebsite)
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
                "asp.net", "asp.net core", ".net core",
                "blazor", "c#"
            }))
            {
                score += 20;
                reasons.Add("Atbilst C# / ASP.NET prasmēm");
            }

            if (HasAny(text, new[]
            {
                "landing page", "portfolio website",
                "site vitrine", "site portfolio"
            }))
            {
                score += 10;
                reasons.Add("Landing page vai portfolio");
            }

            if (HasAny(text, new[]
            {
                "linux server", "nginx", "vps",
                "hosting setup", "server deployment"
            }))
            {
                score += 10;
                reasons.Add("Servera uzstādīšanas darbi");
            }

            if (HasAny(text, new[]
            {
                "wordpress", "woocommerce",
                "shopify", "prestashop"
            }))
            {
                score += 5;
                reasons.Add("CMS vai e-komercijas tehnoloģija");
            }

            if (HasAny(cleanTitle, buildKeywords) ||
                HasAny(cleanTitle, redesignKeywords))
            {
                score += 10;
                reasons.Add("Projekta mērķis skaidrs virsrakstā");
            }

            score = Math.Min(score, 100);

            bool qualified =
                score >= 35 &&
                (isBuild || isRedesign ||
                 (hasWebsite && hasRequest));

            return new LeadQualificationResult
            {
                IsQualified = qualified,
                Score = score,
                Reason = qualified
                    ? string.Join("; ", reasons)
                    : "Nepietiekamu pasūtījuma pazīmju: " +
                      string.Join("; ", reasons)
            };
        }

        // JAUNAIS ALGORITMS 2.0
        // Pagaidām izmantojams tikai testiem un auditam.
        public LeadQualificationResult EvaluateV2(
            string title,
            string description)
        {
            string text = Normalize($"{title} {description}");
            string heading = Normalize(title);

            var orderReasons = new List<string>();
            var skillReasons = new List<string>();

            string[] jobWords =
            {
                "we are hiring", "full-time employee",
                "permanent position", "employee benefits",
                "annual salary", "join our team",
                "offre d'emploi", "recrutement cdi",
                "вакансія", "повна зайнятість"
            };

            string[] offerWords =
            {
                "i offer website development",
                "i provide web development services",
                "hire me for web development",
                "je propose mes services",
                "j'offre mes services",
                "предлагаю услуги разработки",
                "надаю послуги розробки"
            };

            bool isJob = HasAny(text, jobWords);
            bool isOffer = HasAny(text, offerWords);

            if (isJob || isOffer)
            {
                string reason = isJob
                    ? "Darba vakances pazīmes"
                    : "Pakalpojumu piedāvājuma pazīmes";

                return new LeadQualificationResult
                {
                    Score = 0,
                    IsQualified = false,
                    Reason = reason,
                    OrderConfidence = 0,
                    SkillMatch = 0,
                    OrderReason = reason,
                    SkillReason = "Nav vērtēts"
                };
            }

            string[] websiteWords =
            {
                "website", "web site", "web app",
                "landing page", "e-commerce", "ecommerce",
                "online store", "shopify", "wordpress",
                "woocommerce", "prestashop",
                "site web", "site internet",
                "site vitrine", "boutique",
                "mājaslap", "tīmekļa vietn",
                "сайт", "сайту", "сайта",
                "вебсайт", "магазин",
                "інтернет-магазин"
            };

            string[] requestWords =
            {
                "i need", "we need", "looking for",
                "looking to hire", "i want to",
                "we want to", "seeking",
                "je cherche", "recherche expert",
                "recherche développeur",
                "recherche developpeur",
                "j'ai besoin", "nous avons besoin",
                "je souhaiterais", "nous souhaitons",
                "je voudrais", "besoin de",
                "meklēju", "nepieciešams",
                "ищу", "нужен", "нужно",
                "шукаємо", "шукаю",
                "потрібен", "потрібно"
            };

            string[] buildWords =
            {
                "create a website", "build a website",
                "website development", "website creation",
                "develop a website", "new website",
                "création de site", "création d'un site",
                "créer un site", "creer un site",
                "développement de site",
                "mājaslapas izstrāde",
                "izveidot mājaslapu",
                "создание сайта", "разработка сайта",
                "створення сайту", "розробка сайту"
            };

            string[] changeWords =
            {
                "redesign", "rebuild", "modernize",
                "website update", "website changes",
                "website fixes", "fix website",
                "bug fixes", "maintenance",
                "refonte", "modifs", "modifications",
                "correction d'erreurs",
                "correction de bugs", "correction de bug",
                "mise à jour", "maintenance du site",
                "modernisation", "mājaslapas pārveide",
                "labot mājaslapu", "atjaunot mājaslapu",
                "редизайн", "обновление",
                "исправление ошибок",
                "оновлення", "підтримки",
                "підтримка", "виправлення помилок"
            };

            bool hasWebsite = HasAny(text, websiteWords);
            bool hasRequest = HasAny(text, requestWords);
            bool hasBuild = HasAny(text, buildWords);
            bool hasChanges = HasAny(text, changeWords);

            int confidence = 0;

            if (hasWebsite)
            {
                confidence += 20;
                orderReasons.Add("Mājaslapas vai e-komercijas tēma");
            }

            if (hasRequest)
            {
                confidence += 40;
                orderReasons.Add("Klienta pieprasījuma formulējums");
            }

            if (hasBuild)
            {
                confidence += 30;
                orderReasons.Add("Vietnes izstrādes uzdevums");
            }

            if (hasChanges)
            {
                confidence += 30;
                orderReasons.Add("Pārveides, labošanas vai uzturēšanas uzdevums");
            }

            if (hasWebsite &&
                (HasAny(heading, buildWords) ||
                 HasAny(heading, changeWords)))
            {
                confidence += 10;
                orderReasons.Add("Konkrēts uzdevums virsrakstā");
            }

            // Ja nav nevienas pieprasījuma vai darba pazīmes,
            // tehnoloģiju pieminēšana viena pati nav pietiekama.
            if (!hasRequest && !hasBuild && !hasChanges)
                confidence = Math.Min(confidence, 20);

            confidence = Math.Clamp(confidence, 0, 100);

            int skill = 0;

            if (hasWebsite)
            {
                skill += 20;
                skillReasons.Add("Mājaslapu izstrādes joma");
            }

            if (hasBuild)
            {
                skill += 20;
                skillReasons.Add("Jaunas vietnes izstrāde");
            }

            if (hasChanges)
            {
                skill += 15;
                skillReasons.Add("Esošas vietnes uzlabošana");
            }

            if (HasAny(text, new[]
            {
                "asp.net", ".net core", "blazor", "c#",
                "asp net"
            }))
            {
                skill += 40;
                skillReasons.Add("C# / ASP.NET tehnoloģijas");
            }

            if (HasAny(text, new[]
            {
                "linux", "nginx", "vps",
                "server deployment", "hosting setup"
            }))
            {
                skill += 20;
                skillReasons.Add("Linux / serveru uzstādīšana");
            }

            if (HasAny(text, new[]
            {
                "landing page", "site vitrine",
                "portfolio website", "site portfolio"
            }))
            {
                skill += 20;
                skillReasons.Add("Landing page vai portfolio");
            }

            bool hasCms = HasAny(text, new[]
            {
                "wordpress", "woocommerce",
                "shopify", "prestashop"
            });

            if (hasCms)
            {
                skillReasons.Add(
                    "CMS platforma — nepieciešama manuāla prasmju pārbaude");

                // Specifiskas CMS prasības neuzskatām
                // par automātisku ASP.NET priekšrocību.
                skill = Math.Min(skill, 45);
            }

            skill = Math.Clamp(skill, 0, 100);

            bool isQualified =
                hasWebsite &&
                confidence >= 50;

            // Savietojamības lauki jaunajā režīmā.
            // Score ir prioritātes rādītājs, nevis
            // matemātiska pasūtījuma varbūtība.
            int priority = isQualified
                ? (int)Math.Round(
                    confidence * 0.65 + skill * 0.35)
                : 0;

            string orderReason = orderReasons.Count > 0
                ? string.Join("; ", orderReasons)
                : "Nav skaidru pasūtījuma pazīmju";

            string skillReason = skillReasons.Count > 0
                ? string.Join("; ", skillReasons)
                : "Nav noteikta prasmju atbilstība";

            return new LeadQualificationResult
            {
                IsQualified = isQualified,
                Score = priority,
                Reason = $"Pasūtījums: {orderReason}. " +
                         $"Prasmes: {skillReason}.",
                OrderConfidence = confidence,
                SkillMatch = skill,
                OrderReason = orderReason,
                SkillReason = skillReason
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
            string text = value ?? "";

            // Atkodē arī atkārtoti kodētas HTML entītijas.
            for (int i = 0; i < 2; i++)
                text = WebUtility.HtmlDecode(text);

            text = Regex.Replace(text, "<[^>]*>", " ");
            text = text.ToLowerInvariant();
            text = Regex.Replace(text, @"\s+", " ");

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
        // Esošie lauki
        public bool IsQualified { get; set; }
        public int Score { get; set; }
        public string Reason { get; set; } = "";

        // Jaunie 2.0 lauki
        public int OrderConfidence { get; set; }
        public int SkillMatch { get; set; }
        public string OrderReason { get; set; } = "";
        public string SkillReason { get; set; } = "";
    }
}
