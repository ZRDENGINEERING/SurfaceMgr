using System.Net.Http;
using System.Text.Json;

public class UsgsProduct
{
    public string Title { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string Format { get; set; } = "";
    public string PublicationDate { get; set; } = "";
}

public class UsgsTnmLocator
{
    private const string ProductsUrl = "https://tnmaccess.nationalmap.gov/api/v1/products";
    private readonly HttpClient _http;
    public UsgsTnmLocator(HttpClient http) => _http = http;

    public async Task<List<UsgsProduct>> FindElevationProductsAsync(
        double west, double south, double east, double north,
        string prodFormats = "GeoTIFF")
    {
        var url = $"{ProductsUrl}?bbox={west},{south},{east},{north}" +
                   $"&prodFormats={Uri.EscapeDataString(prodFormats)}" +
                   "&max=50&outputFormat=JSON";

        var json = await _http.GetStringAsync(url);
        using var doc = JsonDocument.Parse(json);

        var results = new List<UsgsProduct>();
        foreach (var item in doc.RootElement.GetProperty("items").EnumerateArray())
        {
            results.Add(new UsgsProduct
            {
                Title = item.GetProperty("title").GetString() ?? "",
                DownloadUrl = item.GetProperty("downloadURL").GetString() ?? "",
                Format = item.TryGetProperty("format", out var f) ? f.GetString() ?? "" : "",
                PublicationDate = item.TryGetProperty("publicationDate", out var d) ? d.GetString() ?? "" : ""
            });
        }
        return results;
    }
}


