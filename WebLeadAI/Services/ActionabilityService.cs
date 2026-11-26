using System;
using System.Collections.Generic;
using System.Linq;
using WebLeadAI.Models;

namespace WebLeadAI.Services;

// Orientējoša klienta uzrunāšanas lietderība. NEMAINA datubāzes prioritāti vai statusu.
public static class ActionabilityService
{
    public const int MinimumRecommendedScore = 55;
    public const int MaximumAgeDays = 30;

    public sealed record ExclusionReason(string Code, string Label, string Detail);

    public sealed record Result(string Category, string Label, string CssClass, int Rank,
        string Explanation, IReadOnlyList<string> Notes)
    {
        // Visi neizpildītie ieteikuma nosacījumi; pirmais ir galvenais iemesls.
        // Piezīmes (piem., nezināms datums) pašas par sevi nav izslēgšanas iemesli.
        public IReadOnlyList<ExclusionReason> ExclusionReasons { get; init; }
            = Array.Empty<ExclusionReason>();
        public ExclusionReason? PrimaryExclusion => ExclusionReasons.FirstOrDefault();
    }

    public static Result Evaluate(Lead lead, DateTime? utcNow = null)
    {
        var now = utcNow ?? DateTime.UtcNow;
        var solo = SoloFitService.Evaluate(lead);
        var notes = new List<string>();
        var procurement = string.Equals(lead.SourceType, "Procurement", StringComparison.OrdinalIgnoreCase);
        var title = (lead.CompanyName ?? "").ToLowerInvariant();
        var description = (lead.Description ?? "").ToLowerInvariant();
        var text = title + " " + description;
        var status = lead.Status ?? "";
        var score = lead.AILeadScore;

        var rejected = status.Equals("Rejected", StringComparison.OrdinalIgnoreCase);
        var contacted = status.Equals("Contacted", StringComparison.OrdinalIgnoreCase);
        var expired = false;
        if (procurement && lead.SubmissionDeadline.HasValue)
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(
                OperatingSystem.IsWindows() ? "FLE Standard Time" : "Europe/Riga");
            expired = lead.SubmissionDeadline.Value < TimeZoneInfo.ConvertTimeFromUtc(now, zone);
        }

        // Valodas signāli dod tikai saziņas piezīmi; tie nebloķē ieteikumu.
        var frenchSignals = new[] { "création d'un site", "créer un site", "site vitrine", "agence de voyage",
            "pour une", "développement d'un", "réservation en ligne", "logiciel de gestion" };
        var frenchLikely = frenchSignals.Any(s => text.Contains(s, StringComparison.OrdinalIgnoreCase));
        var old = lead.PublishedAt.HasValue &&
                  DateTime.SpecifyKind(lead.PublishedAt.Value, DateTimeKind.Utc) < now.AddDays(-MaximumAgeDays);
        var possible = solo.Level == "Iespējams piemērots";
        var low = solo.Level == "Zema solo piemērotība";

        // Kopīgs pamats filtram un diagnostikai. Arī agrīna atgriešanās saglabā
        // pārējos šķēršļus, lai viena iemesla novēršana netiktu uztverta kā garantija.
        var reasons = new List<ExclusionReason>();
        if (rejected)
            reasons.Add(new("rejected", "Jau noraidīts", "Saglabātais statuss ir Rejected."));
        if (contacted)
            reasons.Add(new("contacted", "Jau uzrunāts", "Saglabātais statuss ir Contacted; tas nav jauns uzrunāšanas kandidāts."));
        if (expired)
            reasons.Add(new("expired", "IUB termiņš pagājis",
                $"Saglabātais termiņš: {lead.SubmissionDeadline:dd.MM.yyyy HH:mm} (Latvijas laiks). Pārbaudi grozījumus EIS."));
        if (score <= 0)
            reasons.Add(new("zero_priority", "Prioritāte ir 0 vai mazāka",
                $"Saglabāti {score}/100 punkti; ieteikumam vajag vismaz {MinimumRecommendedScore}."));
        else if (score < MinimumRecommendedScore)
            reasons.Add(new("low_priority", $"Prioritāte zem {MinimumRecommendedScore}",
                $"Saglabāti {score}/100 punkti; ieteikumam vajag vismaz {MinimumRecommendedScore}."));
        if (procurement)
            reasons.Add(new("procurement", "IUB jāizvērtē manuāli",
                "Pašreizējā atlase valsts iepirkumus automātiski neiesaka; jāizlasa nolikums un prasības."));
        if (!possible)
            reasons.Add(new(low ? "solo_low" : "solo_review",
                low ? "Zema solo piemērotība" : "Solo piemērotība nav apstiprināta",
                solo.Summary));
        if (old)
            reasons.Add(new("old", $"Vecāks par {MaximumAgeDays} dienām",
                $"Publicēts {lead.PublishedAt:dd.MM.yyyy HH:mm} UTC; pirms uzrunāšanas jāpārbauda aktualitāte."));

        Result CreateResult(string category, string label, string cssClass, int rank,
            string explanation, IReadOnlyList<string> resultNotes) =>
            new(category, label, cssClass, rank, explanation, resultNotes)
            {
                ExclusionReasons = reasons.AsReadOnly()
            };

        if (rejected)
            return CreateResult("skip", "Nav ieteicams", "secondary", 2,
                "Projekts jau ir noraidīts.", new[] { "Maini statusu tikai tad, ja lēmums tiešām ir mainījies." });

        if (contacted)
            return CreateResult("review", "Jau uzrunāts", "primary", 1,
                "Šis klients jau ir uzrunāts, tāpēc tas nav jauns ieteikums.", new[] { "Pārbaudi, vai vajadzīga atkārtota saziņa." });

        if (expired)
        {
            return CreateResult("skip", "Termiņš pagājis", "secondary", 2,
                "Saglabātais iepirkuma pieteikšanās termiņš ir pagājis.",
                new[] { "Pirms galīga lēmuma pārbaudi, vai EIS nav publicēti grozījumi." });
        }

        if (score <= 0)
            return CreateResult("review", "Jāpārbauda", "warning", 1,
                "Saglabātā prioritāte ir 0 — bez papildu pārbaudes nevar ieteikt uzrunāšanai.",
                new[] { "Pārbaudi avota kvalitāti un vajadzības gadījumā pārvērtē projektu." });

        if (score < 45)
            notes.Add("Zema saglabātā prioritāte — pārbaudi, vai tas tiešām ir klienta pasūtījums.");

        if (frenchLikely)
            notes.Add("Sludinājums, iespējams, ir franču valodā. Vari izmantot tulkošanas palīdzību; pirms pieteikšanās precizē saziņas un izpildes valodas prasības. Valoda ieteikumu nebloķē.");

        if (lead.PublishedAt.HasValue)
        {
            if (old)
                notes.Add("Publicēts vairāk nekā pirms 30 dienām — pieejamība jāpārbauda.");
        }
        else
        {
            notes.Add("Publicēšanas datums nav zināms — aktualitāte jāpārbauda.");
        }

        if (procurement)
            notes.Add("Valsts iepirkumam jāpārbauda nolikums, pieredzes un personāla prasības.");

        if (reasons.Count == 0)
            return CreateResult("recommended", "Ieteicams pārbaudīt", "success", 0,
                "Labs sākotnējs kandidāts uzrunāšanai pēc pieejamības un prasību pārbaudes.", notes);

        if (low && score < 45)
            return CreateResult("skip", "Zema prioritāte", "secondary", 2,
                "Zema solo piemērotība un zema saglabātā prioritāte.", notes);

        return CreateResult("review", "Jāpārbauda", "warning", 1,
            "Pirms uzrunāšanas nepieciešama papildu prasību, aktualitātes vai valodas pārbaude.", notes);
    }
}
