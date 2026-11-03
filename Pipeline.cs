using Microsoft.Extensions.Logging;

namespace FootballHub;

public static class Pipeline
{
    public static async Task RunAsync(ILogger log, int perFeed = 6)
    {
        var store = new SeenStore();
        var seen = new HashSet<string>();

        // Depolama sorun çıkarırsa bülten yine de gitsin: tekrar riski, hiç haber gitmemesinden iyidir
        try
        {
            await store.CleanupAsync(TimeSpan.FromDays(3));
            seen = await store.GetSeenAsync();
        }
        catch (Exception ex)
        {
            log.LogWarning("Görülen haberler okunamadı, tekrar kontrolü bu turda atlandı: {Message}", ex.Message);
        }

        var reader = new FeedReader();
        var results = await Task.WhenAll(FeedSources.All.Select(s => reader.ReadAsync(s.Name, s.Url, log)));

                var fresh = NewsCleaner.Clean(results.SelectMany(r => r), TimeSpan.FromHours(12))
            .Where(n => !seen.Contains(SeenStore.KeyOf(n)))
            .GroupBy(n => n.Feed)
            .SelectMany(g => g.Take(perFeed))
            .ToList();

        log.LogInformation("Yeni haber sayısı: {Count}", fresh.Count);
        if (fresh.Count == 0) return;

        var analyzed = await new Analyzer().AnalyzeAsync(fresh, log);
        if (analyzed.Count == 0)
        {
            log.LogWarning("Analiz sonuç vermedi, haberler işaretlenmedi; sonraki turda tekrar denenecek.");
            return;
        }

        var stories = StoryBuilder.Build(analyzed);
        var planned = DiscordNotifier.BuildMessages(stories).Count;
        var sent = await new DiscordNotifier().SendDigestAsync(stories, log);
        log.LogInformation("{Stories} hikâye, {Sent}/{Planned} Discord mesajı gönderildi", stories.Count, sent, planned);

        // Hiçbir şey gönderilemediyse haberleri "görüldü" yapma, sonraki tur yeniden denesin
        if (planned > 0 && sent == 0)
        {
            log.LogWarning("Discord'a hiçbir mesaj gönderilemedi, haberler işaretlenmedi.");
            return;
        }

        try
        {
            await store.MarkSeenAsync(analyzed.Select(a => SeenStore.KeyOf(a.Item)));
        }
        catch (Exception ex)
        {
            log.LogWarning("Haberler işaretlenemedi: {Message}", ex.Message);
        }
    }
}