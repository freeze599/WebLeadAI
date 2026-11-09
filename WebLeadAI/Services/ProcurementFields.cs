using System.Globalization;
using System.Text.RegularExpressions;
using WebLeadAI.Models;

namespace WebLeadAI.Services;

public static class ProcurementFields
{
    public static void FillMissing(Lead lead, string? description)
    {
        if (lead.SourceType != "Procurement" || string.IsNullOrWhiteSpace(description)) return;
        foreach (var raw in description.Split('\n'))
        {
            var line = raw.Trim();
            if (lead.EstimatedBudget == null && line.StartsWith("Paredzamā vērtība", StringComparison.OrdinalIgnoreCase))
            {
                int colon = line.IndexOf(':');
                if (colon >= 0)
                {
                    var value = line[(colon + 1)..].Replace("EUR", "", StringComparison.OrdinalIgnoreCase)
                        .Replace("\u00a0", "").Replace("\u202f", "").Replace(" ", "").Trim();
                    if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.GetCultureInfo("lv-LV"), out var budget)
                        && budget > 0 && budget < 1_000_000_000_000m)
                        lead.EstimatedBudget = budget;
                }
            }
            if (lead.SubmissionDeadline == null && line.StartsWith("Pieteikšanās termiņš", StringComparison.OrdinalIgnoreCase))
            {
                var match = Regex.Match(line, @"(?<d>\d{2}\.\d{2}\.\d{4}\s+\d{1,2}:\d{2})");
                if (match.Success && DateTime.TryParseExact(match.Groups["d"].Value,
                    new[] { "dd.MM.yyyy HH:mm", "dd.MM.yyyy H:mm" }, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var deadline))
                    lead.SubmissionDeadline = DateTime.SpecifyKind(deadline, DateTimeKind.Unspecified);
            }
            if (string.IsNullOrWhiteSpace(lead.BuyerName) && line.StartsWith("Pasūtītājs:", StringComparison.OrdinalIgnoreCase))
            {
                var buyer = line["Pasūtītājs:".Length..].Trim();
                if (buyer.Length is > 0 and <= 300) lead.BuyerName = buyer;
            }
        }
    }
}
