using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure;
using Azure.AI.Inference;
using Microsoft.Extensions.Logging;

namespace FootballHub;

public record AnalyzedNews(NewsItem Item, string Lig, string Kategori, int Guvenilirlik, bool Spam);

public class Analyzer
{
    private const string SystemPrompt =
    "Sen bir erkek futbolu haber analistisin. Sana numaralı haber başlıkları verilecek. " +
    "Her başlık için bir nesne içeren bir JSON DİZİSİ döndür, başka hiçbir metin yazma. " +
    "Nesne şeması: {\"id\":1,\"lig\":\"...\",\"kategori\":\"...\",\"guvenilirlik\":1,\"spam\":false}. " +
    "'lig': haberin konusu olan KULÜBÜN oynadığı lig (Süper Lig, La Liga, Bundesliga, Şampiyonlar Ligi, Premier Lig veya Diğer). " +
    "Haber farklı liglerden iki kulübün karşılaşması ile ilgiliyse 'Şampiyonlar Ligi' yaz. " +
    "Oyuncunun eski takımının ligini YAZMA. " +
    "Kadın futbolu veya kadın takımlarıyla ilgili haberlerde 'lig' alanına 'Diğer' yaz. " +
    "'kategori': transfer, maç, sakatlık, bahis (oran, tahmin, iddaa haberleri) veya diğer. " +
    "'guvenilirlik' 1-4: 4 = resmi açıklama, 3 = somut haber, 2 = iddia veya söylenti dili, 1 = tıklama tuzağı. " +
    "'spam': yalnızca reklam amaçlı içerik, bilet veya yayın rehberi ya da içeriksiz tıklama tuzağıysa true; " +
    "bahis oranı ve tahmin haberleri dahil gerçek bir futbol haberiyse false.";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly ChatCompletionsClient _client;
    private readonly string _model;

    public Analyzer()
    {
        var endpoint = Environment.GetEnvironmentVariable("AI_ENDPOINT")
            ?? throw new InvalidOperationException("AI_ENDPOINT eksik");
        var key = Environment.GetEnvironmentVariable("AI_KEY")
            ?? throw new InvalidOperationException("AI_KEY eksik");
        _model = Environment.GetEnvironmentVariable("AI_MODEL")
            ?? throw new InvalidOperationException("AI_MODEL eksik");

        _client = new ChatCompletionsClient(new Uri(endpoint), new AzureKeyCredential(key));
    }

    private class AiResult
    {
        public int Id { get; set; }
        public string? Lig { get; set; }
        public string? Kategori { get; set; }
        public int Guvenilirlik { get; set; }
        public bool Spam { get; set; }
        public string? Ozet { get; set; }
    }

    private async Task<List<AnalyzedNews>> AnalyzeBatchAsync(List<NewsItem> items, ILogger log)
    {
        var list = new StringBuilder();
        for (int i = 0; i < items.Count; i++)
            list.AppendLine($"{i + 1}. [{items[i].Source}] {items[i].Title}");

        var options = new ChatCompletionsOptions
        {
            Model = _model,
            Temperature = 0.2f,
            MaxTokens = 3000,
            Messages =
            {
                new ChatRequestSystemMessage(SystemPrompt),
                new ChatRequestUserMessage(list.ToString())
            }
        };

        try
        {
            Response<ChatCompletions> response = await CompleteWithRetryAsync(options, log);
            var text = response.Value.Content ?? "";

            var start = text.IndexOf('[');
            var end = text.LastIndexOf(']');
            if (start < 0 || end <= start)
            {
                log.LogWarning("Modelden JSON dizisi alınamadı. Cevap: {Text}", text);
                return new List<AnalyzedNews>();
            }

            var parsed = JsonSerializer.Deserialize<List<AiResult>>(text[start..(end + 1)], JsonOpts)
                         ?? new List<AiResult>();

            var result = new List<AnalyzedNews>();
            foreach (var r in parsed)
            {
                var idx = r.Id - 1;
                if (idx < 0 || idx >= items.Count) continue;

                result.Add(new AnalyzedNews(
                    items[idx],
                    NormalizeLig(r.Lig),
                    NormalizeKategori(r.Kategori),
                    Math.Clamp(r.Guvenilirlik, 1, 4),
                    r.Spam));
            }
            return result;
        }
        catch (Exception ex)
        {
            log.LogError("Analiz başarısız: {Message}", ex.Message);
            return new List<AnalyzedNews>();
        }
    }
    public async Task<List<AnalyzedNews>> AnalyzeAsync(List<NewsItem> items, ILogger log)
    {
        var all = new List<AnalyzedNews>();

        // 15'erlik gruplar halinde gönder, aralara kısa bekleme koy
        foreach (var batch in items.Chunk(15))
        {
            all.AddRange(await AnalyzeBatchAsync(batch.ToList(), log));
            await Task.Delay(3000);
        }

        return all;
    }

    private async Task<Response<ChatCompletions>> CompleteWithRetryAsync(ChatCompletionsOptions options, ILogger log)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await _client.CompleteAsync(options);
            }
            catch (RequestFailedException ex) when (ex.Status == 429 && attempt < 4)
            {
                var wait = TimeSpan.FromSeconds(20 * attempt);
                log.LogWarning("Hız sınırı (429), {Seconds} sn beklenip tekrar denenecek (deneme {Attempt}/3)",
                    wait.TotalSeconds, attempt);
                await Task.Delay(wait);
            }
        }
    }
    private static string NormalizeLig(string? lig)
    {
        var l = (lig ?? "").ToLowerInvariant();
        if (l.Contains("süper") || l.Contains("super")) return "Süper Lig";
        if (l.Contains("la liga")) return "La Liga";
        if (l.Contains("bundes")) return "Bundesliga";
        if (l.Contains("şampiyonlar") || l.Contains("sampiyonlar") || l.Contains("champions")) return "Şampiyonlar Ligi";
        if (l.Contains("premier")) return "Premier Lig";
        return "Diğer";
    }

    private static string NormalizeKategori(string? kategori)
    {
        var k = (kategori ?? "").ToLowerInvariant().Trim();
        return k is "transfer" or "maç" or "sakatlık" or "bahis" ? k : "diğer";
    }
}