using System.IO;
using System.Net.Http;

public class MergerDEMs
{
    /// TxGIO path — downloads and extracts zipped .img tiles first.
    public async Task<string> DownloadMergeAndClipAsync(
    List<TxgioResource> quadrantDems, string quadName,
    ClipBoundary boundary, int targetEpsg, string workDir)
    {
        Directory.CreateDirectory(workDir);
        var imgPaths = new List<string>();

        using var http = new HttpClient();
        foreach (var res in quadrantDems)
        {
            var zipPath = Path.Combine(workDir, Path.GetFileName(res.ResourceUrl));
            var bytes = await http.GetByteArrayAsync(res.ResourceUrl);
            await File.WriteAllBytesAsync(zipPath, bytes);

            var extractDir = Path.Combine(workDir, Path.GetFileNameWithoutExtension(zipPath));
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);

            // A single tile's zip can contain multiple .img sub-tiles (e.g. a
            // 4x4 grid a1..d4) — take ALL of them, not just the first found.
            var imgs = Directory.GetFiles(extractDir, "*.img", SearchOption.AllDirectories);
            imgPaths.AddRange(imgs);
        }

        if (imgPaths.Count == 0)
            throw new InvalidOperationException("No .img files extracted.");

        var vrtPath = Path.Combine(workDir, $"{quadName}.vrt");
        var cutlinePath = Path.Combine(workDir, $"{quadName}_cutline.geojson");
        var tifPath = Path.Combine(workDir, $"{quadName}_clipped.tif");

        File.WriteAllText(cutlinePath, boundary.ToGeoJson());

        RunGdal("gdalbuildvrt", $"\"{vrtPath}\" {string.Join(" ", imgPaths.Select(p => $"\"{p}\""))}");
        RunGdal("gdalwarp",
            $"-overwrite -t_srs EPSG:{targetEpsg} " +
            $"-cutline \"{cutlinePath}\" -cutline_srs EPSG:{targetEpsg} -crop_to_cutline " +
            $"-of GTiff -co COMPRESS=DEFLATE \"{vrtPath}\" \"{tifPath}\"");

        return tifPath;
    }


    /// USGS path — streams directly via GDAL's /vsicurl/, no local download.
    public async Task<string> DownloadMergeAndClipStreamedAsync(
        List<string> resourceUrls, string label,
        ClipBoundary boundary, int targetEpsg, string workDir)
    {
        Directory.CreateDirectory(workDir);

        var vsiPaths = resourceUrls.Select(url => $"/vsicurl/{url}").ToList();

        var vrtPath = Path.Combine(workDir, $"{label}.vrt");
        var cutlinePath = Path.Combine(workDir, $"{label}_cutline.geojson");
        var tifPath = Path.Combine(workDir, $"{label}_clipped.tif");

        File.WriteAllText(cutlinePath, boundary.ToGeoJson());

        RunGdal("gdalbuildvrt", $"\"{vrtPath}\" {string.Join(" ", vsiPaths.Select(p => $"\"{p}\""))}");
        RunGdal("gdalwarp",
            $"-overwrite -t_srs EPSG:{targetEpsg} " +
            $"-cutline \"{cutlinePath}\" -cutline_srs EPSG:{targetEpsg} -crop_to_cutline " +
            $"-of GTiff -co COMPRESS=DEFLATE \"{vrtPath}\" \"{tifPath}\"");

        return tifPath;
    }

    private void RunGdal(string exe, string args)
    {
        const string projData = @"C:\OSGeo4W\share\proj";
        const string gdalData = @"C:\OSGeo4W\share\gdal";
        var exePath = $@"C:\OSGeo4W\bin\{exe}.exe";

        var psi = new System.Diagnostics.ProcessStartInfo(exePath, args)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        psi.EnvironmentVariables["PROJ_LIB"] = projData;
        psi.EnvironmentVariables["PROJ_DATA"] = projData;
        psi.EnvironmentVariables["GDAL_DATA"] = gdalData;

        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.WaitForExit();
        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"{exe} failed: {proc.StandardError.ReadToEnd()}");
    }
}