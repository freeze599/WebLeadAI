namespace WebLeadAI.Services
{
    // Deterministic templates: no external API, automatic sending or database writes.
    public static class LeadProposalService
    {
        public static string Build(string? title, string language)
        {
            string project = LeadContentText.ToPlainText(title ?? "");
            if (string.IsNullOrWhiteSpace(project))
                project = language == "lv" ? "Jūsu mājaslapas projekts"
                    : language == "fr" ? "Votre projet de site web" : "Your website project";

            return language switch
            {
                "lv" => $@"Labdien!

Vēlos pārrunāt Jūsu projektu “{project}”.

Izstrādāju mājaslapas ar C# un ASP.NET Core. Varu izvērtēt Jūsu prasības un piedāvāt piemērotu risinājumu.

Pirms cenas un termiņa piedāvājuma vēlos precizēt:
• Kādas lapas un funkcijas ir nepieciešamas?
• Vai ir jāizmanto noteikta platforma vai tehnoloģija?
• Vai teksti, attēli un dizaina materiāli jau ir sagatavoti?
• Kāds ir vēlamais izpildes termiņš un budžets?

Manu darbu piemēri: [ievieto portfolio saites].

Ja projekts vēl ir aktuāls, labprāt pārrunāšu detaļas.

Ar cieņu,
[ievieto savu vārdu]",
                "fr" => $@"Bonjour,

Je vous contacte au sujet de votre projet « {project} ».

Je développe des sites web avec C# et ASP.NET Core. Je serais ravi d'étudier vos besoins et de vous proposer une solution adaptée.

Avant de vous proposer un tarif et un délai, pourriez-vous préciser :
• Quelles pages et fonctionnalités souhaitez-vous ?
• Faut-il utiliser une plateforme ou une technologie particulière ?
• Les textes, les images et les éléments graphiques sont-ils déjà disponibles ?
• Quels sont votre délai souhaité et votre budget ?

Exemples de mes réalisations : [ajoutez les liens de votre portfolio].

Si votre projet est toujours d'actualité, je serais heureux d'en discuter avec vous.

Cordialement,
[ajoutez votre nom]",
                _ => $@"Hello,

I am contacting you about your project “{project}”.

I develop websites using C# and ASP.NET Core. I would be happy to review your requirements and discuss a suitable solution.

Before proposing a price and timeline, could you clarify:
• Which pages and features do you need?
• Is a particular platform or technology required?
• Are the text, images and design materials ready?
• What are your preferred deadline and budget?

Examples of my work: [add your portfolio links].

If the project is still available, I would welcome the opportunity to discuss the details.

Best regards,
[add your name]"
            };
        }
    }
}
