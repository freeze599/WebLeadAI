using System.Net;
using System.Net.Mail;
using System.Text;

namespace WebLeadAI.Services;

// Optional outbound email alert. Credentials and recipient belong in User Secrets,
// not appsettings.json or source control. No mail is sent unless explicitly enabled.
public sealed class NewLeadEmailNotifier(
    IConfiguration config,
    ILogger<NewLeadEmailNotifier> logger)
{
    public async Task<string?> SendAsync(
        IReadOnlyCollection<NewLeadNotification> newLeads,
        CancellationToken cancellationToken)
    {
        if (!config.GetValue("LeadNotifications:Enabled", false))
            return "E-pasta paziņojumi nav ieslēgti.";

        int minScore = Math.Clamp(config.GetValue("LeadNotifications:MinimumScore", 60), 0, 100);
        var selected = newLeads
            .Where(x => x.Score >= minScore && IsAllowedFreelancehuntLink(x.Url))
            .OrderByDescending(x => x.Score)
            .ToList();
        if (selected.Count == 0) return null;

        string host = config["LeadNotifications:Smtp:Host"] ?? "";
        string? recipient = config["LeadNotifications:To"];
        string? from = config["LeadNotifications:From"];
        string? username = config["LeadNotifications:Smtp:Username"];
        string? password = config["LeadNotifications:Smtp:Password"];
        int port = config.GetValue("LeadNotifications:Smtp:Port", 587);

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from) ||
            string.IsNullOrWhiteSpace(recipient) || string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(password) || port is < 1 or > 65535 ||
            !MailAddress.TryCreate(from, out _) || !MailAddress.TryCreate(recipient, out _))
        {
            logger.LogWarning("WebLeadAI e-pasta paziņojumi ieslēgti, bet SMTP konfigurācija ir nepilnīga.");
            return "E-pasts nav nosūtīts: pārbaudi SMTP iestatījumus (User Secrets).";
        }

        var body = new StringBuilder();
        body.AppendLine($"WebLeadAI atrada {selected.Count} jaunus Freelancehunt kandidātus ar vērtējumu vismaz {minScore}/100.");
        body.AppendLine("Pieteikšanās pieejamība un projekta prasības jāpārbauda oriģinālajā sludinājumā.");
        body.AppendLine();
        foreach (var item in selected)
        {
            body.AppendLine($"{CleanLine(item.Title)} — {item.Score}/100");
            body.AppendLine(item.Url);
            if (item.PublishedAt.HasValue)
                body.AppendLine($"Publicēts (avota datums): {item.PublishedAt.Value:yyyy-MM-dd HH:mm}");
            body.AppendLine();
        }
        body.AppendLine("Automātiski pieteikumi netika nosūtīti.");

        using var message = new MailMessage(new MailAddress(from!), new MailAddress(recipient!))
        {
            Subject = $"WebLeadAI: {selected.Count} jauni Freelancehunt kandidāti",
            Body = body.ToString(),
            IsBodyHtml = false,
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8
        };
        using var client = new SmtpClient(host, port)
        {
            EnableSsl = true,  // STARTTLS, typically port 587; not implicit TLS on 465.
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(username, password),
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = 15000
        };

        // The SMTP protocol does not support cancellation on all stages with SmtpClient.
        cancellationToken.ThrowIfCancellationRequested();
        await client.SendMailAsync(message, cancellationToken);
        logger.LogInformation("WebLeadAI nosūtīja e-pasta paziņojumu par {Count} jauniem projektiem.", selected.Count);
        return $"E-pastā nosūtīti {selected.Count} jauni kandidāti.";
    }

    private static bool IsAllowedFreelancehuntLink(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        return uri.Scheme == Uri.UriSchemeHttps &&
               (uri.Host.Equals("freelancehunt.com", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.EndsWith(".freelancehunt.com", StringComparison.OrdinalIgnoreCase));
    }

    private static string CleanLine(string? value) => (value ?? "")
        .Replace('\r', ' ').Replace('\n', ' ').Trim();
}
