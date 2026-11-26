using WebLeadAI.Models;

namespace WebLeadAI.Services;

// Platformas piekļuve nav projekta kvalitātes vērtējums. Pārbaudīts 26.11.2026.
public static class SourceAccessService
{
    public sealed record Access(string Code, string Label, string Detail)
    {
        public bool NoUpfrontPayment => Code is "free" or "limited";
        public string CssClass => Code switch { "free" => "success", "limited" => "info", "paid" => "secondary", _ => "warning" };
    }
    public sealed record Source(string Name, string Url, string Region, string Kind,
        Access Access, string Import, string Evidence);

    private static Access Free(string detail) => new("free", "Bez priekšapmaksas", detail);
    private static Access Unknown(string detail) => new("unknown", "Piekļuve jāpārbauda", detail);
    private static Access Paid(string detail) => new("paid", "Jāmaksā pirms darba", detail);
    public static readonly Access Unverified = Unknown("Bezmaksas pieteikšanās nav pārbaudīta. Pirms uzrunāšanas pārbaudi avota noteikumus.");
    public static readonly IReadOnlyList<Source> Sources = new Source[]
    {
        new("SS.com", "https://www.ss.com/lv/work/are-required/programmer/", "Latvija", "Projekti / vakances",
            Free("Saziņa sludinājuma lapā; sava maksas sludinājuma ievietošana nav vajadzīga. Pārbaudi konkrētā piedāvājuma nosacījumus."),
            "Automātiski: programmētāju RSS un pilnais teksts. Saglabā tikai vietņu pasūtījumu kandidātus; ne visas vakances.", "https://www.ss.com/lv/work/are-required/programmer/"),
        new("IUB / EIS", "https://www.eis.gov.lv/EKEIS/Supplier", "Latvija", "Iepirkumi",
            Unknown("Publiska informācija. Katram iepirkumam atsevišķi pārbaudi nolikumu, kvalifikāciju un iespējamo piedāvājuma nodrošinājumu."),
            "Automātiski: esošais IUB imports. Ieteikšanai nepieciešama manuāla izvērtēšana.", "https://www.eis.gov.lv/EKEIS/Supplier"),
        new("Hubstaff Talent", "https://hubstafftalent.net/", "Starptautiski", "Projekti / līgumdarbi",
            Free("Platforma bez maksas un komisijas. Vajadzīgs apstiprināts profils; ir pieteikumu limiti."),
            "Manuāli. Automātiskā piekļuve šajā vidē atgrieza HTTP 403; imports nav ieslēgts.", "https://support.hubstaff.com/hubstaff-talent-faq/"),
        new("Truelancer", "https://www.truelancer.com/freelance-jobs", "Starptautiski", "Projekti",
            new("limited", "Bezmaksas limits", "20 bezmaksas pieteikumi mēnesī; 8–10% komisija par apmaksāto darbu un iespējamas izmaksas naudas izņemšanai. Papildu pieteikumi maksā."),
            "Manuāli. Automātiskā piekļuve šajā vidē atgrieza HTTP 403; imports nav ieslēgts.", "https://www.truelancer.com/legal/fee-charges"),
        new("Djinni", "https://djinni.co/jobs/", "Ukraina / starptautiski", "IT vakances / līgumdarbi",
            Free("Kandidātiem bez abonementa. Pārbaudi darba veidu un iespēju strādāt no Latvijas."),
            "Manuāli. Automātisks imports vēl nav ieviests.", "https://play.google.com/store/apps/details?id=com.djinnico.djinni"),
        new("Dalder.lv", "https://www.dalder.lv/", "Latvija", "Sludinājumi",
            Unknown("Jāpārbauda tieša saziņa. Pakalpojumu sniedzēju reklāmas nav klientu pasūtījumi."), "Manuāli; imports nav ieviests.", "https://www.dalder.lv/"),
        new("Freelancehunt", "https://freelancehunt.com/", "Ukraina / starptautiski", "Projekti",
            Unknown("Ir bezmaksas pieteikumu limits, bet obligātās profila verifikācijas izmaksas Latvijas lietotājam vēl nav noskaidrotas."),
            "Rezervē; imports nav ieslēgts.", "https://help-ua.freelancehunt.com/uk/pochatok/rieiestratsiia"),
        new("Work.ua", "https://www.work.ua/", "Ukraina", "Vakances",
            Unverified, "Rezervē; jāizvērtē pieteikšanās un importa nosacījumi.", "https://www.work.ua/"),
        new("Robota.ua", "https://robota.ua/", "Ukraina", "Vakances",
            Unverified, "Rezervē; jāizvērtē pieteikšanās un importa nosacījumi.", "https://robota.ua/"),
        new("Himalayas", "https://himalayas.app/", "Starptautiski", "Attālinātas vakances",
            Free("Pieteikšanās ietilpst bezmaksas plānā. Maksas papildfunkcijas nav vajadzīgas; pārbaudi konkrētā darba prasības."),
            "Manuāli. Publisks API ir pieejams, bet imports vēl nav ieviests.", "https://himalayas.app/pricing"),
        new("Jobicy", "https://jobicy.com/jobs", "Starptautiski", "Attālinātas vakances",
            Free("Sludinājumu pārlūkošana un pieteikšanās saites bez pieteikuma maksas."),
            "Automātiski: publiskais Jobicy API. Līdz 500 jaunākajiem inženierijas piedāvājumiem; atlasa tīmekļa līgumdarbus/nepilnu slodzi ar Latviju ietverošu reģiona norādi. Kešatmiņa 30 minūtes.", "https://jobicy.com/jobs"),
        new("Codeur", "https://www.codeur.com/", "Francija", "Projekti",
            Paid("Pieteikumiem un klientu saziņai vajadzīgs abonements."), "Imports izslēgts. Esošie ieraksti saglabāti.", "https://www.codeur.com/subscriptions"),
        new("Freelance.ua", "https://freelance.ua/", "Ukraina", "Projekti",
            Paid("Pamatplānā nav pieteikumu; vajadzīgs PRO. Šis nav Freelancehunt."), "Imports izslēgts. Esošie ieraksti saglabāti.", "https://freelance.ua/services/pro"),
        new("Freelancer.com", "https://www.freelancer.com/", "Starptautiski", "Projekti",
            Paid("Pirms pieteikšanās nepieciešams minimālais konta atlikums; tas neatbilst nosacījumam bez priekšapmaksas."), "Imports izslēgts. Esošie ieraksti saglabāti.", "https://www.freelancer.com/feesandcharges")
    };

    public static Access Evaluate(Lead lead) => ForUrl(lead.SourceUrl);
    public static Access ForUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && uri.Scheme != "http")) return Unverified;
        var host = uri.IdnHost.TrimEnd('.');
        if (host.Equals("ss.lv", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".ss.lv", StringComparison.OrdinalIgnoreCase))
            return Sources[0].Access;
        if (host.Equals("iub.gov.lv", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".iub.gov.lv", StringComparison.OrdinalIgnoreCase))
            return Sources[1].Access;
        return Sources.FirstOrDefault(s =>
        {
            var domain = new Uri(s.Url).Host.Replace("www.", "");
            return host.Equals(domain, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
        })?.Access ?? Unverified;
    }
}
