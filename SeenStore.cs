using System.Security.Cryptography;
using System.Text;
using Azure.Data.Tables;

namespace FootballHub;

public class SeenStore
{
    private const string Partition = "news";
    private readonly TableClient _table;

    public SeenStore()
    {
        var conn = Environment.GetEnvironmentVariable("AzureWebJobsStorage")
            ?? throw new InvalidOperationException("AzureWebJobsStorage eksik");
        _table = new TableClient(conn, "seennews");
    }

    // Aynı başlık = aynı anahtar (harf ve rakamlar dışındaki her şey yok sayılır)
    public static string KeyOf(NewsItem item)
    {
        var normalized = new string(item.Title.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash)[..32];
    }

    public async Task<HashSet<string>> GetSeenAsync()
    {
        await _table.CreateIfNotExistsAsync();
        var seen = new HashSet<string>();
        await foreach (var e in _table.QueryAsync<TableEntity>(
            filter: $"PartitionKey eq '{Partition}'", select: new[] { "RowKey" }))
        {
            seen.Add(e.RowKey);
        }
        return seen;
    }

    public async Task MarkSeenAsync(IEnumerable<string> keys)
    {
        await _table.CreateIfNotExistsAsync();
        foreach (var key in keys.Distinct())
            await _table.UpsertEntityAsync(new TableEntity(Partition, key));
    }

    // Tablo şişmesin: 3 günden eski kayıtları sil (haberler 24 saatten eskiyse zaten gelmiyor)
    public async Task CleanupAsync(TimeSpan olderThan)
    {
        await _table.CreateIfNotExistsAsync();
        var cutoff = DateTimeOffset.UtcNow - olderThan;
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {Partition} and Timestamp lt {cutoff}");

        await foreach (var e in _table.QueryAsync<TableEntity>(filter: filter, select: new[] { "RowKey" }))
            await _table.DeleteEntityAsync(e.PartitionKey, e.RowKey);
    }
}