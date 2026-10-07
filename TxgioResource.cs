using System.Net.Http;
using System.Text.Json;

public class TxgioResource
{
    public string ResourceUrl { get; set; } = "";
    public string AreaTypeName { get; set; } = "";
    public string CollectionId { get; set; } = "";
}

public class TxgioDemLocator
{
    private const string ResourcesUrl = "https://api.tnris.org/api/v1/resources";
    private static readonly string[] Quadrants = { "NE", "NW", "SE", "SW" };

    private readonly HttpClient _http;
    public TxgioDemLocator(HttpClient http) => _http = http;

    public async Task<List<TxgioResource>> FindQuadDemsAsync(
        string quadName, IProgress<string>? progress = null)
    {
        var wanted = Quadrants.Select(q => $"{quadName}|{q}").ToHashSet();
        var found = new Dictionary<string, TxgioResource>();

        string? nextUrl = $"{ResourcesUrl}?limit=1000";
        int page = 0;

        while (nextUrl != null && found.Count < wanted.Count)
        {
            page++;
            progress?.Report($"Scanning page {page}...");

            var json = await _http.GetStringAsync(nextUrl);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            foreach (var r in root.GetProperty("results").EnumerateArray())
            {
                var areaTypeName = r.GetProperty("area_type_name").GetString() ?? "";
                var abbrev = r.GetProperty("resource_type_abbreviation").GetString() ?? "";

                if (abbrev == "DEM" && wanted.Contains(areaTypeName) &&
                    !found.ContainsKey(areaTypeName))
                {
                    found[areaTypeName] = new TxgioResource
                    {
                        ResourceUrl = r.GetProperty("resource").GetString() ?? "",
                        AreaTypeName = areaTypeName,
                        CollectionId = r.GetProperty("collection_id").GetString() ?? ""
                    };
                }
            }

            nextUrl = root.TryGetProperty("next", out var n) && n.ValueKind == JsonValueKind.String
                ? n.GetString()
                : null;
        }

        return found.Values.ToList();
    }

    public async Task<List<TxgioResource>> FindQuadLazAsync(
        string quadName, IProgress<string>? progress = null)
    {
        var wanted = Quadrants.Select(q => $"{quadName}|{q}").ToHashSet();
        var found = new Dictionary<string, TxgioResource>();

        string? nextUrl = $"{ResourcesUrl}?limit=1000";
        int page = 0;

        while (nextUrl != null && found.Count < wanted.Count)
        {
            page++;
            progress?.Report($"Scanning page {page}...");

            var json = await _http.GetStringAsync(nextUrl);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            foreach (var r in root.GetProperty("results").EnumerateArray())
            {
                var areaTypeName = r.GetProperty("area_type_name").GetString() ?? "";
                var abbrev = r.GetProperty("resource_type_abbreviation").GetString() ?? "";

                if (abbrev == "LPC" && wanted.Contains(areaTypeName) &&
                    !found.ContainsKey(areaTypeName))
                {
                    found[areaTypeName] = new TxgioResource
                    {
                        ResourceUrl = r.GetProperty("resource").GetString() ?? "",
                        AreaTypeName = areaTypeName,
                        CollectionId = r.GetProperty("collection_id").GetString() ?? ""
                    };
                }
            }

            nextUrl = root.TryGetProperty("next", out var n) && n.ValueKind == JsonValueKind.String
                ? n.GetString()
                : null;
        }

        return found.Values.ToList();
    }
}