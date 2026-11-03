using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace FootballHub;

public class HourlyDigest
{
    private readonly ILogger<HourlyDigest> _logger;

    public HourlyDigest(ILogger<HourlyDigest> logger) => _logger = logger;

    [Function("HourlyDigest")]
    public async Task Run([TimerTrigger("%DIGEST_SCHEDULE%")] TimerInfo timer)
    {
        _logger.LogInformation("Bülten başladı: {Time}", DateTimeOffset.Now);
        await Pipeline.RunAsync(_logger);
        _logger.LogInformation("Bülten tamamlandı.");
    }
}