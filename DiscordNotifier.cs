using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace FootballHub;

public record DigestMessage(string Lig, string Title, string Description, int Color);

public class DiscordNotifier
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private static readonly (string Lig, string Emoji, int Color)[] Leagues =
    {
        ("Süper Lig", "🇹🇷", 0xE30A17),
        ("La Liga", "🇪🇸", 0xF1BF00),
        ("Bundesliga", "🇩🇪", 0xD20515),
        ("Şampiyonlar Ligi", "🏆", 0x0A3D91),
    };

    public static List<DigestMessage> BuildMessages(List<Story> stories)
    {
        var list = new List<DigestMessage>();
        foreach (var (lig, emoji, color) in Leagues)
        {
            var top = stories.Where(s => s.Lig == lig).Take(5).ToList();
            if (top.Count == 0) continue;

            list.Add(new DigestMessage(lig, $"{emoji} {lig}",
                string.Join("\n\n", top.Select(FormatStory)), color));
        }
        return list;
    }

    public async Task<int> SendDigestAsync(List<Story> stories, ILogger log)
    {
        var webhookUrl = Environment.GetEnvironmentVariable("DISCORD_WEBHOOK_URL");
        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            log.LogWarning("DISCORD_WEBHOOK_URL eksik, gönderim atlandı.");
            return 0;
        }

        int sent = 0;
        foreach (var m in BuildMessages(stories))
        {
            try
            {
                var payload = new
                {
                    embeds = new[]
                    {
                        new
                        {
                            title = m.Title,
                            description = m.Description,
                            color = m.Color,
                            footer = new { text = $"Football Intelligence Hub • {DateTime.Now:dd.MM.yyyy HH:mm}" }
                        }
                    }
                };

                using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var response = await Http.PostAsync(webhookUrl, content);

                if (response.IsSuccessStatusCode) sent++;
                else log.LogWarning("Discord gönderimi başarısız ({Lig}): {Status}", m.Lig, (int)response.StatusCode);
            }
            catch (HttpRequestException ex)
            {
                log.LogWarning("Discord'a ulaşılamadı: {Message}", ex.Message);
                break; // Ağ sorunu varsa diğer ligleri denemenin anlamı yok
            }

            await Task.Delay(1000); // Discord hız sınırına takılmamak için
        }

        return sent;
    }

    private static string FormatStory(Story s)
    {
        var title = s.Title.Replace('[', '(').Replace(']', ')');
        if (title.Length > 120) title = title[..117] + "...";

        var link = s.Items[0].Item.Link;
        var headline = string.IsNullOrEmpty(link) ? title : $"[{title}]({link})";

        var sources = s.Sources.Count == 1 ? s.Sources[0] : $"{s.Sources[0]} +{s.Sources.Count - 1}";

        // Güvenilirlik rozeti sadece transfer iddialarında anlamlı
        var badge = s.Kategori switch
        {
            "transfer" => s.Guvenilirlik switch
            {
                4 => "🟢 Yüksek güven",
                3 => "🟡 Orta güven",
                2 => "🟠 Düşük güven",
                _ => "🔴 Teyitsiz iddia"
            },
            "bahis" => "🎲 Bahis",
            "sakatlık" => "🩹 Sakatlık",
            "maç" => "⚽ Maç",
            _ => "📰 Haber"
        };

        return $"{badge} · {sources}\n{headline}";
    }
}