using System;
using System.Collections.Generic;
using System.Linq;
using WebLeadAI.Models;

namespace WebLeadAI.Services;

// Orientējoša klienta uzrunāšanas lietderība. NEMAINA datubāzes prioritāti vai statusu.
public static class ActionabilityService
{
    public sealed record Result(string Category, string Label, string CssClass, int Rank,
        string Explanation, IReadOnlyList<string> Notes);

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

        if (status.Equals("Rejected", StringComparison.OrdinalIgnoreCase))
            return new Result("skip", "Nav ieteicams", "secondary", 2,
                "Projekts jau ir noraidīts.", new[] { "Maini statusu tikai tad, ja lēmums tiešām ir mainījies." });

        if (status.Equals("Contacted", StringComparison.OrdinalIgnoreCase))
            return new Result("review", "Jau uzrunāts", "primary", 1,
                "Šis klients jau ir uzrunāts, tāpēc tas nav jauns ieteikums.", new[] { "Pārbaudi, vai vajadzīga atkārtota saziņa." });

        if (procurement && lead.SubmissionDeadline.HasValue)
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(
                OperatingSystem.IsWindows() ? "FLE Standard Time" : "Europe/Riga");
            var localNow = TimeZoneInfo.ConvertTimeFromUtc(now, zone);
            if (lead.SubmissionDeadline.Value < localNow)
                return new Result("skip", "Termiņš pagājis", "secondary", 2,
                    "Saglabātais iepirkuma pieteikšanās termiņš ir pagājis.",
                    new[] { "Pirms galīga lēmuma pārbaudi, vai EIS nav publicēti grozījumi." });
        }

        if (score <= 0)
            return new Result("review", "Jāpārbauda", "warning", 1,
                "Saglabātā prioritāte ir 0 — bez papildu pārbaudes nevar ieteikt uzrunāšanai.",
                new[] { "Pārbaudi avota kvalitāti un vajadzības gadījumā pārvērtē projektu." });

        if (score < 45)
            notes.Add("Zema saglabātā prioritāte — pārbaudi, vai tas tiešām ir klienta pasūtījums.");

        // Valodu nenosakām kategoriski. Atpazīstam tikai stipras franču teksta pazīmes.
        var frenchSignals = new[] { "création d'un site", "créer un site", "site vitrine", "agence de voyage",
            "pour une", "développement d'un", "réservation en ligne", "logiciel de gestion" };
        var frenchLikely = frenchSignals.Any(s => text.Contains(s, StringComparison.OrdinalIgnoreCase));
        if (frenchLikely)
            notes.Add("Sludinājums, iespējams, ir franču valodā — pārbaudi saziņas un izpildes valodas prasības.");

        if (lead.PublishedAt.HasValue)
        {
            var publishedUtc = lead.PublishedAt.Value.Kind == DateTimeKind.Utc
                ? lead.PublishedAt.Value : DateTime.SpecifyKind(lead.PublishedAt.Value, DateTimeKind.Utc);
            if (publishedUtc < now.AddDays(-30))
                notes.Add("Publicēts vairāk nekā pirms 30 dienām — pieejamība jāpārbauda.");
        }
        else
        {
            notes.Add("Publicēšanas datums nav zināms — aktualitāte jāpārbauda.");
        }

        if (procurement)
            notes.Add("Valsts iepirkumam jāpārbauda nolikums, pieredzes un personāla prasības.");

        var possible = solo.Level == "Iespējams piemērots";
        var low = solo.Level == "Zema solo piemērotība";
        var old = lead.PublishedAt.HasValue &&
                  DateTime.SpecifyKind(lead.PublishedAt.Value, DateTimeKind.Utc) < now.AddDays(-30);
        if (!procurement && possible && score >= 55 && !frenchLikely && !old)
            return new Result("recommended", "Ieteicams pārbaudīt", "success", 0,
                "Labs sākotnējs kandidāts uzrunāšanai pēc pieejamības un prasību pārbaudes.", notes);

        if (low && score < 45)
            return new Result("skip", "Zema prioritāte", "secondary", 2,
                "Zema solo piemērotība un zema saglabātā prioritāte.", notes);

        return new Result("review", "Jāpārbauda", "warning", 1,
            "Pirms uzrunāšanas nepieciešama papildu prasību, aktualitātes vai valodas pārbaude.", notes);
    }
}
