namespace FootballHub;

public static class SourceWeights
{
    // Resmî kurum, haber ajansı ve kulüp siteleri
    private static readonly string[] Official =
    {
        "anadolu ajansı", "galatasaray.org", "fenerbahce.org", "fenerbahçe sk",
        "bjk.com.tr", "trabzonspor.org.tr", "uefa", "bundesliga.com", "laliga", "tff.org"
    };

    // Yerleşik yayıncılar
    private static readonly string[] Established =
    {
        "bbc", "sky sports", "ntv", "hürriyet", "milliyet", "sabah", "sözcü", "cumhuriyet",
        "habertürk", "fanatik", "fotomaç", "trt", "reuters", "marca", "kicker",
        "the guardian", "espn", "bein"
    };

    public static int Weight(string source)
    {
        var s = source.ToLowerInvariant();
        if (Official.Any(s.Contains)) return 3;
        if (Established.Any(s.Contains)) return 2;
        return 1;
    }

    public static bool IsOfficial(string source) => Weight(source) == 3;
}