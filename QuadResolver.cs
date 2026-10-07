using System.Net.Http;
using System.Text.Json;
using ProjNet.CoordinateSystems.Transformations;

public class QuadResolver
{
    private const string BaseUrl =
        "https://feature.geographic.texas.gov/arcgis/rest/services/Basemap/USGS_Index_Grid/MapServer";

    private readonly HttpClient _http;
    private readonly ICoordinateTransformation _toWgs84;

    private QuadResolver(HttpClient http, ICoordinateTransformation toWgs84)
    {
        _http = http;
        _toWgs84 = toWgs84;
    }

    public static async Task<QuadResolver> CreateAsync(HttpClient http, int sourceEpsg)
    {
        var toWgs84 = await CoordTransform.ToWgs84Async(http, sourceEpsg);
        return new QuadResolver(http, toWgs84);
    }

    public async Task<QuadInfo?> ResolveByPointAsync(double x, double y)
    {
        double[] lonLat = _toWgs84.MathTransform.Transform(new[] { x, y });
        return await QueryQuadAsync(
            $"geometry={lonLat[0]},{lonLat[1]}&geometryType=esriGeometryPoint" +
            "&inSR=4326&spatialRel=esriSpatialRelIntersects");
    }

    public async Task<QuadInfo?> ResolveByNameAsync(string quadName) =>
        await QueryQuadAsync($"where=UPPER(quad_name)=UPPER('{Escape(quadName)}')");

    public async Task<QuadInfo?> ResolveByNumAsync(string quadNum) =>
        await QueryQuadAsync($"where=quad_num='{Escape(quadNum)}'");

    private async Task<QuadInfo?> QueryQuadAsync(string geometryOrWhereClause)
    {
        var url = $"{BaseUrl}/3/query?{geometryOrWhereClause}" +
                   "&outFields=quad_num,quad_name&returnGeometry=false&f=json";

        var json = await _http.GetStringAsync(url);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("features", out var features) ||
            features.GetArrayLength() == 0)
            return null;

        var attrs = features[0].GetProperty("attributes");
        return new QuadInfo
        {
            QuadNum = attrs.GetProperty("quad_num").GetString() ?? "",
            QuadName = attrs.GetProperty("quad_name").GetString() ?? ""
        };
    }

    private static string Escape(string s) => s.Replace("'", "''");
}