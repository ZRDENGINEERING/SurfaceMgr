using System.Net.Http;
using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;

public static class CoordTransform
{
    /// Fetches WKT for the given EPSG code and builds a transform to WGS84.
    /// No zone hardcoded — works for whatever CivilCoordSystem resolves.
    public static async Task<ICoordinateTransformation> ToWgs84Async(HttpClient http, int sourceEpsg)
    {
        var wkt = await http.GetStringAsync($"https://epsg.io/{sourceEpsg}.wkt");

        var csFactory = new CoordinateSystemFactory();
        var sourceCs = csFactory.CreateFromWkt(wkt);
        var wgs84 = GeographicCoordinateSystem.WGS84;

        var ctFactory = new CoordinateTransformationFactory();
        return ctFactory.CreateFromCoordinateSystems(sourceCs, wgs84);
    }
}