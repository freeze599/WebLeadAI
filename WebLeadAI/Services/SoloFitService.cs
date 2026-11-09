using System;
using System.Collections.Generic;
using System.Linq;
using WebLeadAI.Models;

namespace WebLeadAI.Services;

// Piesardzīgs ieteikums, nevis pretendenta tiesiskās atbilstības apliecinājums.
public static class SoloFitService
{
    public sealed record Assessment(string Level, string CssClass, string Complexity,
        string Summary, IReadOnlyList<string> Notes);

    public static Assessment Evaluate(Lead lead)
    {
        var text = $"{lead.CompanyName} {LeadContentText.ForEvaluation(lead.Description)}".ToLowerInvariant();
        var notes = new List<string>();
        var isProcurement = lead.SourceType.Equals("Procurement", StringComparison.OrdinalIgnoreCase);

        bool Includes(params string[] keys) => keys.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
        var highRisk = Includes("marketplace", "multi-vendor", "multivendor", "enterprise", "erp", "banking",
            "medical records", "hospital information", "e-veselīb", "valsts informācijas sistēm",
            "kritiskā infrastruktūr", "24/7", "diennakts atbalst", "iso 27001", "penetration test");
        var teamRequirement = Includes("komandas pieredz", "projekta komanda", "vismaz 3 speciālist",
            "vismaz trīs speciālist", "obligāta sertifikāc", "required certifications",
            "team of developers", "multiple developers");
        var simple = Includes("landing page", "portfolio website", "site vitrine", "vizītkaršu mājaslap",
            "one-page website", "one page website", "nelielas mājaslap", "small business website");
        var moderate = Includes("e-commerce", "ecommerce", "internetveikal", "rezervāc", "booking",
            "integrāc", "payment gateway", "api integration", "user accounts", "lietotāju kont");
        var dotnet = Includes("asp.net", "blazor", "c#", ".net core");
        var outsideStack = Includes("wordpress", "shopify", "wix", "drupal", "magento");

        if (dotnet) notes.Add("Minētas C#/.NET tehnoloģijas — iespējama prasmju sakritība.");
        if (outsideStack) notes.Add("Minēta cita platforma — pārbaudi savu praktisko pieredzi ar to.");
        if (highRisk) notes.Add("Aprakstā ir pazīmes, kas var prasīt lielu apjomu, papildu drošību vai atbalstu.");
        if (teamRequirement) notes.Add("Aprakstā iespējama komandas vai sertifikācijas prasība — jāpārbauda dokumenti.");
        if (moderate) notes.Add("Iespējamas integrācijas vai papildfunkcijas — darba apjoms jāprecizē.");

        if (isProcurement)
        {
            if (lead.EstimatedBudget is >= 50000)
                notes.Add("Liela paredzamā līgumcena — tā neapliecina obligātu komandas prasību, bet prasa detalizētu izvērtēšanu.");
            if (lead.SubmissionDeadline is DateTime deadline && deadline < DateTime.Now)
                notes.Add("Saglabātais pieteikšanās termiņš ir pagājis; pārbaudi aktuālo statusu EIS.");
            notes.Add("IUB kopsavilkums nav pilns nolikums: pārbaudi pieredzes, personāla, finanšu un nodrošinājuma prasības.");
            return new Assessment("Jāpārbauda", "warning", highRisk || teamRequirement ? "Iespējams augsts" : "Nav droši nosakāms",
                "No publiskā kopsavilkuma nevar droši noteikt, vai iepirkumu var īstenot viens izstrādātājs.", notes);
        }
        if (highRisk || teamRequirement)
            return new Assessment("Zema solo piemērotība", "danger", "Augsta",
                "Aprakstā ir pazīmes par sarežģītu projektu vai papildu resursu prasībām.", notes);
        if (simple && !moderate && !outsideStack)
            return new Assessment("Iespējams piemērots", "success", "Zema vai vidēja",
                "Apraksts izskatās pēc nelielas tīmekļa vietnes; apjoms un termiņi vēl jāapstiprina.", notes);
        return new Assessment("Jāpārbauda", "warning", moderate ? "Vidēja vai augsta" : "Nav droši nosakāms",
            "Aprakstā nav pietiekami daudz detaļu, lai novērtētu solo izpildes iespējamību.", notes);
    }
}
