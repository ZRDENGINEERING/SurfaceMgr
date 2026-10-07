using System.Net.Http;
using System.Text.Json;
using ProjNet.CoordinateSystems.Transformations;

public class QQuadTile
{
    public string DoqqNum { get; set; } = "";
    public string Quadrant { get; set; } = "";
}

public class TxQuadGridLocator
{
    private const string BaseUrl =
        "https://feature.geographic.texas.gov/arcgis/rest/services/Basemap/USGS_Index_Grid/MapServer/4";

    private readonly HttpClient _http;
    public TxQuadGridLocator(HttpClient http) => _http = http;

    public async Task<List<QQuadTile>> FindTilesByBoundaryAsync(
    ClipBoundary boundary, ICoordinateTransformation toWgs84)
    {
        var lonLatPoints = boundary.Vertices
            .Select(v =>
            {
                double[] lonLat = toWgs84.MathTransform.Transform(new[] { v.X, v.Y });
                return (lon: lonLat[0], lat: lonLat[1]);
            })
            .ToList();

        // Esri REST expects exterior rings in clockwise order. Compute signed
        // area to detect winding and reverse if the ring is counter-clockwise.
        double signedArea = 0;
        for (int i = 0; i < lonLatPoints.Count - 1; i++)
        {
            var p1 = lonLatPoints[i];
            var p2 = lonLatPoints[i + 1];
            signedArea += (p2.lon - p1.lon) * (p2.lat + p1.lat);
        }
        if (signedArea < 0) // negative = counter-clockwise in this formula's convention
            lonLatPoints.Reverse();

        var rings = lonLatPoints.Select(p => $"[{p.lon},{p.lat}]");
        var polygonJson = $"{{\"rings\":[[{string.Join(",", rings)}]],\"spatialReference\":{{\"wkid\":4326}}}}";

        var url = $"{BaseUrl}/query?geometry={Uri.EscapeDataString(polygonJson)}" +
                   "&geometryType=esriGeometryPolygon&inSR=4326" +
                   "&spatialRel=esriSpatialRelIntersects" +
                   "&outFields=doqq_num,qq_txt&returnGeometry=false&f=json";

        var json = await _http.GetStringAsync(url);
        using var doc = System.Text.Json.JsonDocument.Parse(json);

        var results = new List<QQuadTile>();
        if (!doc.RootElement.TryGetProperty("features", out var features)) return results;

        foreach (var f in features.EnumerateArray())
        {
            var attrs = f.GetProperty("attributes");
            results.Add(new QQuadTile
            {
                DoqqNum = attrs.GetProperty("doqq_num").GetString() ?? "",
                Quadrant = attrs.GetProperty("qq_txt").GetString() ?? ""
            });
        }
        return results;
    }
}