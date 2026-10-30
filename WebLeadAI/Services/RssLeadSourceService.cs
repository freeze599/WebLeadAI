                    }

                    _logger.LogInformation(
                        "RSS avots {Source}: nolasīti {Count} ieraksti.",
                        feed.Key,
                        count);
                }
                catch (Exception ex) when (
                    ex is HttpRequestException ||
                    ex is XmlException ||
                    ex is TaskCanceledException ||
                    ex is InvalidOperationException)
                {
                    feedResult.Error = ex.Message;
                    _logger.LogWarning(
                        ex,
                        "RSS avots {Source} nav pieejams.",
                        feed.Key);
                }
            }

            return results;
        }
    }

    public class RssLeadItem
    {
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Url { get; set; } = "";
        public string Source { get; set; } = "";
        public DateTime? PublishedAt { get; set; }
    }
}

namespace WebLeadAI.Services
{
    public class RssFeedResult
    {
        public string Source { get; set; } = "";
        public List<RssLeadItem> Items { get; set; } = new();
        public string? Error { get; set; }
    }
}


namespace WebLeadAI.Services
{
    // Kopīga apstrāde RSS importam, detaļu skatam un algoritmam 2.x.
    // Rezultāts ir parasts teksts. Razor skatā to izvada bez Html.Raw.
    public static class LeadContentText
    {
        private static string Replace(string text, string pattern, string replacement)
        {
            return Regex.Replace(text, pattern, replacement,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));
        }

        public static string ToPlainText(string? value)
        {
            string text = value ?? "";
            // Atkodē arī atkārtoti kodētus RSS fragmentus.
            for (int i = 0; i < 3; i++)
            {
                string decoded = WebUtility.HtmlDecode(text);
                if (decoded == text)
                    break;
                text = decoded;
            }

            text = Replace(text, @"<!--[^>]*?-->", " ");
            text = Replace(text, @"<(script|style)\b[^>]*>[\s\S]*?</\1\s*>", " ");
            text = Replace(text, @"<br\b[^>]*>|</?(?:p|div|li|ul|ol|h[1-6]|tr|section)\b[^>]*>", "\n");
            // Neaiztiek parastu tekstu, piemēram, 'budget < 500'.
            text = Replace(text, @"</?[a-z][^>]*>", " ");
            text = text.Replace('\u00a0', ' ').Replace("\r\n", "\n").Replace('\r', '\n');
            text = Replace(text, @"[^\S\n]+", " ");
            text = Replace(text, @" *\n *", "\n");
            text = Replace(text, @"\n{3,}", "\n\n");
            return text.Trim();
        }

        public static string ForEvaluation(string? value)
        {
            string text = ToPlainText(value);

            // Codeur RSS: atsevišķa budžeta/kategoriju rindkopa, nevis klienta prasības.
            // Dzēš tikai pazīstamu rindas formātu; neskaidru tekstu saglabā.
            string withoutHeader = Replace(text,
                @"(?m)^Budget\s*:[^\n]*?\s[-–—]\s*Cat[eé]gories\s*:[^\n]*(?:\n|$)", "");
            if (!string.IsNullOrWhiteSpace(withoutHeader))
                text = withoutHeader;

            text = Replace(text,
                @"(?m)^Voir ce projet sur Codeur\s*\.?\s*$", "");

            // Freelancer RSS beigās pievieno budžetu un 'Jobs:' birkas.
            // Tās neapstiprina, ka visas uzskaitītās tehnoloģijas ir obligātas.
            text = Replace(text,
                @"\s*\(Budget\s*:[^()]*,\s*Jobs\s*:[^()]*\)\s*$", "");
            return text.Trim();
        }
    }
}
