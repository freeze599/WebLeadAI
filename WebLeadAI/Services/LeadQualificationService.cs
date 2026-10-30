
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

        // ALGORITMS 2.3 — prioritāte pēc prasmēm; RSS metadati izslēgti; 1.0 nemainīts.
        // IsQualified = ticams klienta pasūtījums, nevis prasmju garantija.
        public LeadQualificationResult EvaluateV2(string title, string description)
        {
            string heading = Normalize(title);
            // Vērtē klienta tekstu, nevis RSS kategorijas un platformas birkas.
            string requestDescription = LeadContentText.ForEvaluation(description);
            string text = Normalize($"{title} {requestDescription}");
            var orderReasons = new List<string>();
            var skillReasons = new List<string>();

            // Vispirms pārbaudām sludinājuma veidu, nevis tehnoloģijas.
            string[] jobWords =
            {
                "we are hiring", "we're hiring", "now hiring", "join our team",
                "full-time employee", "full time employee", "full-time position",
                "permanent position", "employee benefits", "annual salary",
                "job opening", "job vacancy", "employment contract",
                "apply for this position", "apply for the role", "career opportunity",
                "offre d'emploi", "recrutement cdi", "poste à pourvoir",
                "rejoignez notre équipe", "вакансія", "повна зайнятість",
                "постоянная работа в офисе", "вакансия", "полная занятость"
            };
            string[] offerWords =
            {
                "i offer website development", "i provide web development services",
                "hire me for web development", "my web development services",
                "je propose mes services", "j'offre mes services",
                "je suis développeur freelance", "предлагаю услуги разработки",
                "надаю послуги розробки"
            };

            bool jobTitle = Regex.IsMatch(heading,
                @"\b(senior|junior|mid-level|middle|lead|staff|principal)\b.{0,65}\b(developer|engineer|programmer)\b")
                || Regex.IsMatch(heading,
                @"\b(full[- ]?stack|front[- ]?end|back[- ]?end)\s+(developer|engineer)\b")
                || Regex.IsMatch(heading,
                @"\b(developer|engineer)\s+(position|vacancy|job|role)\b")
                || Regex.IsMatch(heading,
                @"\b(développeur|разработчик|розробник)\b.{0,40}\b(senior|junior|cdi|вакансия|вакансія)\b");

            if (HasAny(text, jobWords) || jobTitle)
                return RejectV2("Darba vakances pazīmes");
            if (HasAny(text, offerWords))
                return RejectV2("Pakalpojumu piedāvājuma pazīmes");

            string[] websiteWords =
            {
                "website", "web site", "web app", "landing page", "e-commerce",
                "ecommerce", "online store", "online shop", "shopify", "wordpress",
                "woocommerce", "prestashop", "joomla", "cms", "site web",
                "site internet", "site vitrine", "boutique", "mājaslap",
                "tīmekļa vietn", "сайт", "сайту", "сайта", "вебсайт",
                "магазин", "інтернет-магазин", "интернет-магазин"
            };
            string[] requestWords =
            {
                "i need", "we need", "i want", "we want", "i have an",
                "looking for", "looking to hire", "seeking", "je cherche",
                "recherche expert", "recherche développeur", "recherche developpeur",
                "j'ai besoin", "nous avons besoin", "je souhaiterais",
                "nous souhaitons", "je voudrais", "nous voudrions", "besoin de",
                "meklēju", "nepieciešams", "vēlos", "ищу", "нужен",
                "нужно", "хочу", "шукаємо", "шукаю", "потрібен", "потрібно"
            };
            string[] buildWords =
            {
                "create a website", "build a website", "website development",
                "website creation", "develop a website", "new website",
                "création de site", "création d'un site", "créer un site",
                "creer un site", "développement de site", "mājaslapas izstrāde",
                "izveidot mājaslapu", "создание сайта", "разработка сайта",
                "створення сайту", "розробка сайту"
            };
            string[] changeWords =
            {
                "redesign", "rebuild", "modernize", "website update",
                "website changes", "website fixes", "fix website", "bug fixes",
                "maintenance", "refonte", "modifs", "modifications",
                "correction d'erreurs", "correction de bugs", "correction de bug",
                "mise à jour", "maintenance du site", "modernisation",
                "mājaslapas pārveide", "labot mājaslapu", "atjaunot mājaslapu",
                "редизайн", "обновление", "исправление ошибок",
                "оновлення", "підтримки", "підтримка", "виправлення помилок"
            };
            string[] migrationWords =
            {
                "migration", "migrer", "migrate", "migrating", "upgrade",
                "version upgrade", "pārcelt vietni", "migrācija", "миграция",
                "перенос сайта", "міграція", "перенесення сайту"
            };
            string[] catalogWords =
            {
                "import excel", "import products", "product import", "import inventory",
                "upload products", "publish products", "items published",
                "published in our online store", "product listings", "catalog import",
                "catalogue import", "importer des produits", "importation produits",
                "preču imports", "importēt preces", "импорт товаров", "загрузка товаров",
                "імпорт товарів", "завантаження товарів"
            };

            bool hasWebsite = HasAny(text, websiteWords);
            bool hasRequest = HasAny(text, requestWords);
            bool hasBuild = HasAny(text, buildWords);
            bool hasChanges = HasAny(text, changeWords);
            bool hasMigration = HasAny(text, migrationWords);
            bool hasCatalog = HasAny(text, catalogWords);
            bool concreteTask = hasBuild || hasChanges || hasMigration || hasCatalog;

            // Izņēmums: īss, bet nepārprotams uzdevums virsrakstā.
            // Viena tehnoloģijas nosaukuma vai "looking for" nepietiek.
            bool taskInTitle = HasAny(heading, buildWords) ||
                               HasAny(heading, changeWords) ||
                               HasAny(heading, migrationWords) ||
                               HasAny(heading, catalogWords);

            if (!hasWebsite || !concreteTask)
                return RejectV2("Nav pietiekami konkrēta mājaslapas vai e-komercijas uzdevuma");

            int confidence = 20;
            orderReasons.Add("Mājaslapas vai e-komercijas tēma");

            if (hasRequest)
            {
                confidence += 30;
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
                orderReasons.Add("Vietnes labošanas vai pārveides uzdevums");
            }
            if (hasMigration)
            {
                confidence += 35;
                orderReasons.Add("Vietnes migrācijas uzdevums");
            }
            if (hasCatalog)
            {
                confidence += 35;
                orderReasons.Add("Preču kataloga ievades vai importa uzdevums");
            }
            if (taskInTitle)
            {
                confidence += 15;
                orderReasons.Add("Konkrēts uzdevums virsrakstā");
            }
            confidence = Math.Clamp(confidence, 0, 100);

            // 2.3: darbu veidi dod vienu bāzes vērtējumu, nevis summējas.
            int skill = hasBuild ? 55 : hasChanges ? 45 : hasMigration ? 30 : 20;
            skillReasons.Add(hasBuild
                ? "Jaunas vietnes izstrāde — tehnoloģijas un apjoms jāprecizē"
                : hasChanges ? "Esošas vietnes uzlabošana — jāpārbauda tehnoloģijas"
                : hasMigration ? "Migrācija — jāpārbauda platformas prasmes"
                : "Kataloga darbi — iespējama datu ievade");

            bool hasDotNet = HasAny(text, new[]
            {
                "asp.net", ".net core", "blazor", "c#", "asp net"
            });
            bool hasCms = HasAny(text, new[]
            {
                "wordpress", "woocommerce", "shopify", "prestashop", "joomla", "wix"
            });
            bool hasMarketplace = HasAny(text, new[]
            {
                "marketplace", "multi-vendor", "multi vendor", "multivendor",
                "place de marché", "place de marche", "pakalpojumu platforma",
                "маркетплейс"
            });
            bool hasBookings = HasAny(text, new[]
            {
                "booking", "appointment", "réserver", "reserver", "réservation",
                "reservation", "créneaux", "creneaux", "rezervāc", "rezervac",
                "бронирован", "бронюван"
            });

            if (hasDotNet)
            {
                skill += 30;
                skillReasons.Add("C# / ASP.NET minēts klienta tekstā vai virsrakstā");
            }
            if (HasAny(text, new[] { "linux", "nginx", "vps", "server deployment", "hosting setup" }))
            {
                skill += 10;
                skillReasons.Add("Linux / serveru uzstādīšanas pazīmes");
            }
            if (HasAny(text, new[] { "landing page", "site vitrine", "portfolio website", "site portfolio" }))
            {
                skill += 10;
                skillReasons.Add("Prezentācijas vietne vai landing page");
            }
            if (hasBookings && !hasMarketplace && !hasCms && (hasBuild || hasChanges))
            {
                skill += 10;
                skillReasons.Add("Rezervāciju funkcija — iespējams ASP.NET uzdevums; apjoms jāprecizē");
            }
            if (hasMarketplace)
            {
                skill = Math.Min(skill, hasDotNet ? 60 : 45);
                skillReasons.Add("Daudzpusēja tirdzniecības/pakalpojumu platforma — jāizvērtē apjoms un integrācijas");
            }
            if (hasCms)
            {
                skill = Math.Min(skill, 25);
                skillReasons.Add("Minēta CMS platforma ārpus pamatprasmēm — zema prioritāte līdz manuālai pārbaudei");
            }
            skill = Math.Clamp(skill, 0, 100);

            // Atlase saglabājas pēc pasūtījuma ticamības; prioritāte: 30% pasūtījums, 70% prasmes.
            bool qualified = confidence >= 50;
            int priority = qualified
                ? (int)Math.Round(confidence * 0.30 + skill * 0.70)
                : 0;
            string orderReason = string.Join("; ", orderReasons);
            string skillReason = string.Join("; ", skillReasons);
            return new LeadQualificationResult
            {
                IsQualified = qualified,
                Score = priority,
                Reason = $"Pasūtījums: {orderReason}. Prasmes: {skillReason}.",
                OrderConfidence = confidence,
                SkillMatch = skill,
                OrderReason = orderReason,
                SkillReason = skillReason
            };
        }

        private static LeadQualificationResult RejectV2(string reason)
        {
            return new LeadQualificationResult
            {
                IsQualified = false,
                Score = 0,
                Reason = reason,
                OrderConfidence = 0,
                SkillMatch = 0,
                OrderReason = reason,
                SkillReason = "Nav vērtēts"
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
