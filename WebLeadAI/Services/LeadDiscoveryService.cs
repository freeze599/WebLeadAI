
using Microsoft.EntityFrameworkCore;
using WebLeadAI.Data;
using WebLeadAI.Models;

namespace WebLeadAI.Services
{
    public class LeadDiscoveryService
    {
        private readonly AppDbContext _context;
        private readonly RssLeadSourceService _rssService;
        private readonly LeadQualificationService _qualification;
        private readonly IubLeadSourceService _iub;
        private readonly JobicyLeadSourceService _jobicy;
        private readonly FreelancehuntLeadSourceService _freelancehunt;
        private readonly ILogger<LeadDiscoveryService> _logger;
        private readonly IConfiguration _config;

        public LeadDiscoveryService(
            AppDbContext context,
            RssLeadSourceService rssService,
            LeadQualificationService qualification,
            IubLeadSourceService iub,
            JobicyLeadSourceService jobicy,
            FreelancehuntLeadSourceService freelancehunt,
            ILogger<LeadDiscoveryService> logger, IConfiguration config)
        {
            _context = context;
            _rssService = rssService;
            _qualification = qualification;
            _iub = iub;
            _jobicy = jobicy;
            _freelancehunt = freelancehunt;
            _logger = logger;
            _config = config;
        }

        public async Task<int> DiscoverLeadsAsync()
        {
            var report = await DiscoverWithStatisticsAsync();
            return report.Sum(x => x.Added);
        }

        public Task<List<LeadSourceStatistics>> DiscoverWithStatisticsAsync() => DiscoverInternalAsync(false);

        // Background polling reads ONLY Freelancehunt; no extra Jobicy / SS.com / IUB calls.
        public Task<List<LeadSourceStatistics>> DiscoverFreelancehuntAsync() => DiscoverInternalAsync(true);

        private async Task<List<LeadSourceStatistics>> DiscoverInternalAsync(bool freelancehuntOnly)
        {
            var feeds = new List<RssFeedResult>();
            if (!freelancehuntOnly)
            {
                feeds.AddRange(await _rssService.GetFeedResultsAsync());
                feeds.Add(await _iub.GetFeedResultAsync());
                feeds.Add(await _jobicy.GetFeedResultAsync());
            }
            feeds.Add(await _freelancehunt.GetFeedResultAsync());
            var report = new List<LeadSourceStatistics>();

            var existingLeads = await _context.Leads
                .Where(l => l.SourceUrl != null)
                .ToListAsync();

            var knownUrls = existingLeads
                .GroupBy(
                    l => l.SourceUrl!,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.First(),
                    StringComparer.OrdinalIgnoreCase);

            foreach (var feed in feeds)
            {
                var stats = new LeadSourceStatistics
                {
                    Source = feed.Source,
                    Read = feed.Items.Count,
                    Error = feed.Error,
                    Notice = feed.Notice
                };

                report.Add(stats);

                int missingUrls = 0;
                int nonCandidates = 0;
                int qualificationRejected = 0;
                int emailQueued = 0;

                foreach (var item in feed.Items)
                {
                    if (string.IsNullOrWhiteSpace(item.Url))
                    {
                        missingUrls++;
                        continue;
                    }

                    if (!item.IsCandidate || (feed.Source == "SS.com" && !SsLeadReader.IsProjectRequest(item.Title, item.Description)))
                    {
                        if (!item.IsCandidate) nonCandidates++;
                        if (knownUrls.ContainsKey(item.Url)) stats.Duplicates++;
                        else stats.Rejected++;
                        continue;
                    }

                    // Jaunais vērtēšanas algoritms 2.3
                    var evaluation = _qualification.EvaluateV2(
                        item.Title,
                        item.Description, item.SourceType);

                    if (feed.Source == "IUB")
                    {
                        _logger.LogInformation(
                            "IUB KANDIDĀTS: {Title} | Atbilstošs: {Qualified} | Prioritāte: {Score} | Pamatojums: {Reason} | Saite: {Url}",
                            item.Title.Replace('\r', ' ').Replace('\n', ' '),
                            evaluation.IsQualified, evaluation.Score,
                            evaluation.Reason, item.Url);
                    }

                    if (evaluation.IsQualified)
                        stats.Qualified++;

                    if (knownUrls.TryGetValue(
                        item.Url,
                        out var existingLead))
                    {
                        stats.Duplicates++;

                        // Papildina trūkstošu publicēšanas datumu.
                        // Nemaina statusu un iepriekšējo vērtējumu.
                        if (existingLead.PublishedAt == null &&
                            item.PublishedAt != null)
                        {
                            existingLead.PublishedAt = item.PublishedAt;
                        }

                        // Ja IUB ir publicējis paredzamo vērtību, papildina esošo ierakstu.
                        // Nekad nepārraksta lietotāja statusu, vērtējumu vai melnrakstu.
                        const string budgetLabel = "Paredzamā vērtība (publiskotie IUB dati): ";
                        if (feed.Source == "IUB" &&
                            item.Description.Contains(budgetLabel, StringComparison.Ordinal) &&
                            !(existingLead.Description ?? "").Contains(budgetLabel, StringComparison.Ordinal))
                        {
                            var line = item.Description.Split('\n')
                                .FirstOrDefault(x => x.StartsWith(budgetLabel, StringComparison.Ordinal));
                            if (!string.IsNullOrWhiteSpace(line))
                                existingLead.Description = (existingLead.Description ?? "").TrimEnd() +
                                    "\n" + line;
                        }

                        // Dublikātu gadījumā papildina tikai trūkstošus strukturētos laukus.
                        ProcurementFields.FillMissing(existingLead, item.Description);
                        ProcurementFields.FillMissing(existingLead, existingLead.Description);
                        continue;
                    }

                    if (!evaluation.IsQualified)
                    {
                        qualificationRejected++;
                        stats.Rejected++;
                        continue;
                    }

                    string title = item.Title.Length > 200
                        ? item.Title[..200]
                        : item.Title;

                    var lead = new Lead
                    {
                        CompanyName = title,
                        SourceUrl = item.Url,
                        SourceType = feed.Source == "IUB" ? "Procurement" : item.SourceType,
                        Description = item.Description,
                        Status = "NeedsReview",
                        AILeadScore = evaluation.Score,
                        CreatedAt = DateTime.UtcNow,
                        PublishedAt = item.PublishedAt
                    };

                    ProcurementFields.FillMissing(lead, item.Description);
                    _context.Leads.Add(lead);
                    knownUrls[item.Url] = lead;

                    stats.Added++;
                    stats.NewLeads.Add(new NewLeadNotification(title, item.Url, evaluation.Score, item.PublishedAt));
                    // Enqueue in the SAME SaveChanges as the lead: no lost notifications
                    // between commit and SMTP. Only automatic Freelancehunt discoveries.
                    int alertThreshold = Math.Clamp(_config.GetValue("LeadNotifications:MinimumScore", 60), 0, 100);
                    if (freelancehuntOnly && feed.Source == "Freelancehunt" &&
                        _config.GetValue("LeadNotifications:Enabled", false) &&
                        evaluation.Score >= alertThreshold &&
                        Uri.TryCreate(item.Url, UriKind.Absolute, out var alertUri) &&
                        alertUri.Scheme == Uri.UriSchemeHttps &&
                        (alertUri.Host.Equals("freelancehunt.com", StringComparison.OrdinalIgnoreCase) ||
                         alertUri.Host.EndsWith(".freelancehunt.com", StringComparison.OrdinalIgnoreCase)))
                    {
                        emailQueued++;
                        _context.LeadEmailOutbox.Add(new LeadEmailOutbox
                        {
                            SourceUrl = item.Url,
                            Title = title,
                            Score = evaluation.Score,
                            PublishedAt = item.PublishedAt,
                            CreatedAtUtc = DateTime.UtcNow,
                            NextAttemptUtc = DateTime.UtcNow
                        });
                    }
                }

                if (feed.Source == "Freelancehunt")
                {
                    _logger.LogInformation(
                        "FH_DIAG ATLASE: režīms={Mode}, nolasīti={Read}, kandidātaStatussNoraidīts={NonCandidates}, " +
                        "bezURL={MissingUrls}, kvalifikācijāNoraidīti={QualificationRejected}, " +
                        "kvalificēti={Qualified}, dublikāti={Duplicates}, noraidītiKopā={Rejected}, " +
                        "jauni={Added}, epastiRindā={Queued}, avotaKļūda={Error}",
                        freelancehuntOnly ? "automātisks" : "manuāls", stats.Read,
                        nonCandidates, missingUrls, qualificationRejected, stats.Qualified,
                        stats.Duplicates, stats.Rejected, stats.Added, emailQueued,
                        stats.Error ?? "nav");
                }
            }

            await _context.SaveChangesAsync();

            return report;
        }
    }

    public sealed record NewLeadNotification(string Title, string Url, int Score, DateTime? PublishedAt);

    public class LeadSourceStatistics
    {
        [System.Text.Json.Serialization.JsonIgnore]
        public List<NewLeadNotification> NewLeads { get; set; } = new();
        public string Source { get; set; } = "";
        public int Read { get; set; }
        public int Qualified { get; set; }
        public int Duplicates { get; set; }
        public int Rejected { get; set; }
        public int Added { get; set; }
        public string? Error { get; set; }
        public string? Notice { get; set; }
    }
}
