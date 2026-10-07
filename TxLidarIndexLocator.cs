using System.Net.Http;
using System.Text.Json;

public class LidarTileInfo
{
    public string CollName { get; set; } = "";
    public string CollId { get; set; } = "";
    public string TileId { get; set; } = "";
    public int Year { get; set; }
    public string BestAvail { get; set; } = "";
    public double LpcResCm { get; set; }
    public double DemResM { get; set; }
}

public class TxLidarIndexLocator
{
    private const string BaseUrl =
        "https://feature.geographic.texas.gov/arcgis/rest/services/Status_Maps/Lidar_Index_Public/MapServer/0";

    private readonly HttpClient _http;
    public TxLidarIndexLocator(HttpClient http) => _http = http;

    public async Task<List<LidarTileInfo>> FindTilesByPointAsync(double lon, double lat)
    {
        var url = $"{BaseUrl}/query?geometry={lon},{lat}&geometryType=esriGeometryPoint" +
                   "&inSR=4326&spatialRel=esriSpatialRelIntersects" +
                   "&outFields=collname,collid,tileid,year,bestavail,lpcres,demres" +
                   "&returnGeometry=false&f=json";

        var json = await _http.GetStringAsync(url);
        using var doc = JsonDocument.Parse(json);

        var results = new List<LidarTileInfo>();
        if (!doc.RootElement.TryGetProperty("features", out var features)) return results;

        foreach (var f in features.EnumerateArray())
        {
            var attrs = f.GetProperty("attributes");
            results.Add(new LidarTileInfo
            {
                CollName = attrs.GetProperty("collname").GetString() ?? "",
                CollId = attrs.GetProperty("collid").GetString() ?? "",
                TileId = attrs.GetProperty("tileid").GetString() ?? "",
                Year = attrs.GetProperty("year").GetInt32(),
                BestAvail = attrs.GetProperty("bestavail").GetString() ?? "",
                LpcResCm = attrs.TryGetProperty("lpcres", out var l) && l.ValueKind != JsonValueKind.Null ? l.GetDouble() : 0,
                DemResM = attrs.TryGetProperty("demres", out var d) && d.ValueKind != JsonValueKind.Null ? d.GetDouble() : 0
            });
        }
        return results;
    }
}

public class S3CollectionLocator
{
    private const string BucketUrl = "https://s3.amazonaws.com/data.tnris.org/";
    private readonly HttpClient _http;
    public S3CollectionLocator(HttpClient http) => _http = http;

    public async Task<List<string>> ListCollectionResourcesAsync(string collectionId)
    {
        var keys = new List<string>();
        string? continuationToken = null;

        do
        {
            var url = $"{BucketUrl}?list-type=2&prefix={collectionId}/resources/&max-keys=1000";
            if (continuationToken != null)
                url += $"&continuation-token={Uri.EscapeDataString(continuationToken)}";

            var xml = await _http.GetStringAsync(url);
            var doc = System.Xml.Linq.XDocument.Parse(xml);
            System.Xml.Linq.XNamespace ns = "http://s3.amazonaws.com/doc/2006-03-01/";

            keys.AddRange(doc.Descendants(ns + "Key").Select(e => e.Value));

            var truncated = doc.Descendants(ns + "IsTruncated").FirstOrDefault()?.Value == "true";
            continuationToken = truncated
                ? doc.Descendants(ns + "NextContinuationToken").FirstOrDefault()?.Value
                : null;
        } while (continuationToken != null);

        return keys;
    }
}