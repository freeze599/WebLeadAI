using System.Text.RegularExpressions;

namespace WebLeadAI.Services;

public static class SsLeadReader
{
    // Tikai publiskā programmētāju kategorija. Neatver ārējās saites vai kontaktu atklāšanas adreses.
    public static bool IsAllowedUrl(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u)
        && u.Scheme == "https" && u.Host == "www.ss.com" && u.Port == 443
        && Regex.IsMatch(u.AbsolutePath, @"^/msg/lv/work/are-required/programmer/[a-z]+\.html$", RegexOptions.CultureInvariant)
        && string.IsNullOrEmpty(u.UserInfo) && string.IsNullOrEmpty(u.Query);

    public static string ReadDescription(string html)
    {
        // Lasa tikai teksta daļu pirms atribūtu tabulas, nevis kontaktus, reklāmas vai navigāciju.
        // Ja vietnes struktūra mainās, kļūda ir redzama statistikā; nevērtē RSS saīsinājumu kā pilnu tekstu.
        var match = Regex.Match(html,
            "<div\\b[^>]*\\bid=[\"']msg_div_msg[\"'][^>]*>(?<text>[\\s\\S]*?)<table\\b[^>]*\\bclass=[\"']options_list[\"']",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!match.Success) throw new InvalidOperationException("SS.com pilnā teksta struktūra nav atpazīta.");
        var text = LeadContentText.ToPlainText(match.Groups["text"].Value);
        if (text.Length < 30 || text.Length > 60000)
            throw new InvalidOperationException("SS.com teksts ir tukšs vai neparasta izmēra.");
        return text;
    }

    // Papildu konservatīvs filtrs tieši jauktajai SS.com vakanču kategorijai.
    public static bool IsProjectRequest(string title, string description)
    {
        var text = (title + " " + description).ToLowerInvariant();
        bool Any(params string[] words) => words.Any(text.Contains);
        if (Any("pilnas slodzes", "pilnu slodzi", "aicinām darbā", "pievienojies komandai", "pasniedzēj", "skolotāj", "личного учителя",
            "полный рабочий", "полная занятость", "преподавател", "full-time", "full time")) return false;
        if (Any("piedāvāju mājaslap", "piedāvājam mājaslap", "izstrādājam mājaslap", "предлагаю услуги", "предлагаем услуги")) return false;
        return Any("mājaslap", "mājas lap", "tīmekļa vietn", "interneta veikal", "e-veikal", "website", "web ", "сайт", "интернет-магазин")
            && Any("meklē", "vajadz", "nepieciešam", "vēlos", "pasūt", "ищ", "нуж", "требуется", "looking for", "need")
            && Any("izstrād", "izveid", "pārveid", "uztur", "labot", "uzlab", "programmēt", "gabaldarb", "разработ", "созда", "доработ", "поддерж", "build", "develop", "redesign");
    }
}
