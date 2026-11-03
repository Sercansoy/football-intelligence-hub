using System.Xml;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Xml.Linq;

namespace FootballHub;

public record NewsItem(string Source, string Title, string Link, DateTimeOffset Published, string Feed = "");

public static class FeedSources
{
    private static string GoogleNews(string query) =>
        $"https://news.google.com/rss/search?q={Uri.EscapeDataString(query)}&hl=tr&gl=TR&ceid=TR:tr";

    public static readonly (string Name, string Url)[] All =
    {
        ("BBC Sport", "https://feeds.bbci.co.uk/sport/football/rss.xml"),
        ("Sky Sports", "https://www.skysports.com/rss/11095"),
        ("GNews Süper Lig", GoogleNews("Süper Lig")),
        ("GNews La Liga", GoogleNews("La Liga")),
        ("GNews Bundesliga", GoogleNews("Bundesliga")),
        ("GNews Şampiyonlar Ligi", GoogleNews("Şampiyonlar Ligi")),
    };
}

public class FeedReader
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FootballIntelligenceHub/1.0");
        return client;
    }

    public async Task<List<NewsItem>> ReadAsync(string name, string url, ILogger log)
    {
        try
        {
            var bytes = await Http.GetByteArrayAsync(url);
            using var ms = new MemoryStream(bytes);
            using var xml = XmlReader.Create(ms, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
            var doc = XDocument.Load(xml);

            return doc.Descendants("item")
                .Select(i =>
                {
                    var title = ((string?)i.Element("title"))?.Trim() ?? "";
                    var link = ((string?)i.Element("link"))?.Trim() ?? "";
                    var dateText = (string?)i.Element("pubDate");

                    // Tarih bozuksa haberi atma, "şimdi" say
                    var date = DateTimeOffset.TryParse(dateText, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal, out var d) ? d : DateTimeOffset.UtcNow;

                    return new NewsItem(name, title, link, date, name);
                })
                .Where(n => n.Title.Length > 0)
                .ToList();
        }
        catch (Exception ex)
        {
            log.LogWarning("Kaynak okunamadı: {Name} ({Message})", name, ex.Message);
            return new List<NewsItem>();
        }
    }
}
public static class NewsCleaner
{
    // Haber değeri düşük, SEO amaçlı başlıkları yakalayan basit anahtar kelimeler
    private static readonly string[] LowValueKeywords =
    {
        "nasıl alınır", "bilet fiyatları", "canlı izle", "şifresiz",
        "hangi kanalda", "saat kaçta", "nasıl izlenir","| canlı"
    };
    // Kapsam dışı: kadın futbolu
    private static readonly string[] WomensFootballKeywords =
    {
        "kadın futbol", "kadınlar", "kadın takım", "kadın milli", "kadın süper",
        "bayan futbol", "women", "wsl", "feminin", "femenin", "frauen"
    };

    public static List<NewsItem> Clean(IEnumerable<NewsItem> items, TimeSpan maxAge)
    {
        var tr = new CultureInfo("tr-TR");
        var cutoff = DateTimeOffset.UtcNow - maxAge;
        var seen = new HashSet<string>();
        var result = new List<NewsItem>();

        foreach (var item in items.OrderByDescending(i => i.Published))
        {
            if (item.Published < cutoff) continue;

            var title = item.Title.Trim('\uFEFF', ' ');
            var source = item.Source;

            // Google News: "Başlık - Kaynak" biçimini ayır
            if (source.StartsWith("GNews"))
            {
                var idx = title.LastIndexOf(" - ", StringComparison.Ordinal);
                if (idx > 0)
                {
                    source = title[(idx + 3)..].Trim();
                    title = title[..idx].Trim();
                }
            }

            var lower = title.ToLower(tr);
            if (title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 4) continue;
            if (LowValueKeywords.Any(k => lower.Contains(k))) continue;
            if (WomensFootballKeywords.Any(k => lower.Contains(k))) continue;

            // Tekrar kontrolü: harf ve rakamlardan oluşan sade anahtar
            var key = new string(lower.Where(char.IsLetterOrDigit).ToArray());
            if (!seen.Add(key)) continue;

            result.Add(item with { Title = title, Source = source });
        }

        return result;
    }
}