using System.Globalization;

namespace FootballHub;

public record Story(string Title, string Lig, string Kategori, int Guvenilirlik, List<AnalyzedNews> Items)
{
    public List<string> Sources => Items.Select(i => i.Item.Source).Distinct().ToList();
}

public static class StoryBuilder
{
    private static readonly CultureInfo Tr = new("tr-TR");
    private static readonly char[] Separators = " '’\",.:;!?-|…()[]".ToCharArray();
    private static readonly HashSet<string> Stop = new() { "için", "gibi", "daki", "canlı", "dakika" };

    // Basit "kök" çıkarma: ilk 5 harf (Türkçe ekleri kabaca yok sayar)
    private static HashSet<string> Words(string title) =>
        title.ToLower(Tr).Split(Separators, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 4 && !Stop.Contains(w))
            .Select(w => w.Length > 5 ? w[..5] : w)
            .ToHashSet();

    private static bool Similar(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return false;
        var shared = a.Count(b.Contains);
        return shared >= 3 && shared / (double)Math.Min(a.Count, b.Count) >= 0.5;
    }

    public static List<Story> Build(IEnumerable<AnalyzedNews> analyzed)
    {
        var clusters = new List<(HashSet<string> Words, List<AnalyzedNews> Items)>();

        foreach (var a in analyzed.Where(x => !x.Spam))
        {
            var words = Words(a.Item.Title);
            var match = clusters.FirstOrDefault(c => Similar(c.Words, words));
            if (match.Items != null) match.Items.Add(a);
            else clusters.Add((words, new List<AnalyzedNews> { a }));
        }

        return clusters.Select(c => ToStory(c.Items))
            .OrderByDescending(s => s.Guvenilirlik)
            .ThenByDescending(s => s.Sources.Count)
            .ToList();
    }

    private static Story ToStory(List<AnalyzedNews> items)
    {
        string Majority(Func<AnalyzedNews, string> pick) =>
            items.GroupBy(pick).OrderByDescending(g => g.Count()).First().Key;

        // En güvenilir kaynak başa: başlık ve bağlantı oradan gelsin
        var ordered = items.OrderByDescending(i => SourceWeights.Weight(i.Item.Source)).ToList();

        var sources = ordered.Select(i => i.Item.Source).Distinct().ToList();
        var weighted = sources.Sum(SourceWeights.Weight);
        var hasOfficial = sources.Any(SourceWeights.IsOfficial);
        var kategori = Majority(i => i.Kategori);

        var score = weighted switch { >= 5 => 4, >= 3 => 3, 2 => 2, _ => 1 };

        // Resmî kaynak yoksa transfer iddiası "kesin" sayılamaz
        if (kategori == "transfer" && !hasOfficial) score = Math.Min(score, 3);

        // Model hepsini tıklama tuzağı saydıysa ve resmî kaynak yoksa puan 1
        if (!hasOfficial && items.All(i => i.Guvenilirlik <= 1)) score = 1;

        return new Story(ordered[0].Item.Title, Majority(i => i.Lig), kategori, score, ordered);
    }
}