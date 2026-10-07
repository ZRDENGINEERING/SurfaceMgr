using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

public class LazGroundExtractor
{
    public string ExtractGroundPoints(
        string lazPath, ClipBoundary boundary, string targetEpsg, string outDir,
        string? sourceEpsg = null)
    {
        Directory.CreateDirectory(outDir);

        var enzPath = Path.Combine(outDir,
            Path.GetFileNameWithoutExtension(lazPath) + "_ground.csv");
        var pipelinePath = Path.Combine(outDir, "pipeline.json");

        var pipeline = new
        {
            pipeline = new object[]
            {
                lazPath,
                new Dictionary<string, object>
                {
                    ["type"] = "filters.crop",
                    ["polygon"] = boundary.ToWkt(),
                    ["a_srs"] = $"EPSG:{targetEpsg}"
                },
                BuildReprojectionStage(sourceEpsg, targetEpsg),
                new { type = "filters.range", limits = "Classification[2:2]" },
                new
                {
                    type = "writers.text",
                    format = "csv",
                    order = "X,Y,Z",
                    keep_unspecified = "false",
                    write_header = false,
                    filename = enzPath
                }
            }
        };

        WriteAndRun(pipeline, pipelinePath);
        return enzPath;
    }

    public string DemFromGroundPoints(
        string lazPath, ClipBoundary boundary, string targetEpsg, string outDir,
        double resolution, string? sourceEpsg = null)
    {
        Directory.CreateDirectory(outDir);

        var tifPath = Path.Combine(outDir,
            Path.GetFileNameWithoutExtension(lazPath) + "_ground_dem.tif");
        var pipelinePath = Path.Combine(outDir, "pipeline.json");

        var pipeline = new
        {
            pipeline = new object[]
            {
                lazPath,
                new Dictionary<string, object>
                {
                    ["type"] = "filters.crop",
                    ["polygon"] = boundary.ToWkt(),
                    ["a_srs"] = $"EPSG:{targetEpsg}"
                },
                BuildReprojectionStage(sourceEpsg, targetEpsg),
                new { type = "filters.range", limits = "Classification[2:2]" },
                new
                {
                    type = "writers.gdal",
                    resolution,
                    output_type = "idw",
                    gdaldriver = "GTiff",
                    data_type = "float32",
                    nodata = -9999,
                    filename = tifPath
                }
            }
        };

        WriteAndRun(pipeline, pipelinePath);
        return tifPath;
    }

    public string ExtractGroundPointsMulti(
        List<string> lazPaths, ClipBoundary boundary, string targetEpsg, string outDir,
        string? sourceEpsg = null)
    {
        Directory.CreateDirectory(outDir);

        var enzPath = Path.Combine(outDir, "quad_ground.csv");
        var pipelinePath = Path.Combine(outDir, "pipeline.json");

        var stages = new List<object>();
        stages.AddRange(lazPaths.Select(p => (object)p));
        stages.Add(new { type = "filters.merge" });
        stages.Add(new Dictionary<string, object>
        {
            ["type"] = "filters.crop",
            ["polygon"] = boundary.ToWkt(),
            ["a_srs"] = $"EPSG:{targetEpsg}"
        });
        stages.Add(BuildReprojectionStage(sourceEpsg, targetEpsg));
        stages.Add(new { type = "filters.range", limits = "Classification[2:2]" });
        stages.Add(new
        {
            type = "writers.text",
            format = "csv",
            order = "X,Y,Z",
            keep_unspecified = "false",
            write_header = false,
            filename = enzPath
        });

        var pipeline = new { pipeline = stages };
        WriteAndRun(pipeline, pipelinePath);
        return enzPath;
    }

    public string DemFromGroundPointsMulti(
        List<string> lazPaths, ClipBoundary boundary, string targetEpsg, string outDir,
        double resolution, string? sourceEpsg = null)
    {
        Directory.CreateDirectory(outDir);

        var tifPath = Path.Combine(outDir, "quad_ground_dem.tif");
        var pipelinePath = Path.Combine(outDir, "pipeline.json");

        var stages = new List<object>();
        stages.AddRange(lazPaths.Select(p => (object)p));
        stages.Add(new { type = "filters.merge" });
        stages.Add(new Dictionary<string, object>
        {
            ["type"] = "filters.crop",
            ["polygon"] = boundary.ToWkt(),
            ["a_srs"] = $"EPSG:{targetEpsg}"
        });
        stages.Add(BuildReprojectionStage(sourceEpsg, targetEpsg));
        stages.Add(new { type = "filters.range", limits = "Classification[2:2]" });
        stages.Add(new
        {
            type = "writers.gdal",
            resolution,
            output_type = "idw",
            gdaldriver = "GTiff",
            data_type = "float32",
            nodata = -9999,
            filename = tifPath
        });

        var pipeline = new { pipeline = stages };
        WriteAndRun(pipeline, pipelinePath);
        return tifPath;
    }

    private object BuildReprojectionStage(string? sourceEpsg, string targetEpsg) =>
        sourceEpsg != null
            ? new Dictionary<string, object>
            {
                ["type"] = "filters.reprojection",
                ["in_srs"] = $"EPSG:{sourceEpsg}",
                ["out_srs"] = $"EPSG:{targetEpsg}"
            }
            : new Dictionary<string, object>
            {
                ["type"] = "filters.reprojection",
                ["out_srs"] = $"EPSG:{targetEpsg}"
            };

    private void WriteAndRun(object pipeline, string pipelinePath)
    {
        File.WriteAllText(pipelinePath, JsonSerializer.Serialize(pipeline,
            new JsonSerializerOptions { WriteIndented = true }));
        RunPdal(pipelinePath);
    }

    private const string PdalExe = @"C:\OSGeo4W\bin\pdal.exe";
    private const string ProjData = @"C:\OSGeo4W\share\proj";
    private const string GdalData = @"C:\OSGeo4W\share\gdal";

    /// <summary>
    /// Reads the horizontal EPSG code from a LAZ/LAS header (null if the file has no
    /// usable spatial reference). Reads the header only, so it is fast even on huge tiles.
    /// </summary>
    public string? DetectSourceEpsg(string lazPath)
    {
        var (exit, stdout, _) = RunPdalCapture($"info --metadata \"{lazPath}\"");
        if (exit != 0) return null;

        try
        {
            using var doc = JsonDocument.Parse(stdout);
            if (!doc.RootElement.TryGetProperty("metadata", out var meta) ||
                !meta.TryGetProperty("srs", out var srs) ||
                !srs.TryGetProperty("horizontal", out var horiz))
                return null;

            var wkt = horiz.GetString();
            if (string.IsNullOrEmpty(wkt)) return null;

            // The projected CRS's own AUTHORITY is the last EPSG tag in the horizontal WKT.
            var matches = Regex.Matches(wkt, "AUTHORITY\\[\"EPSG\",\"(\\d+)\"\\]");
            return matches.Count > 0 ? matches[^1].Groups[1].Value : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Works out the source EPSG for a set of point-cloud files. Uses the CRS stored in the
    /// files; falls back to <paramref name="fallbackEpsg"/> (with a log message) when none is
    /// stored; throws if the files disagree, since they can't share one reprojection stage.
    /// </summary>
    public string ResolveSourceEpsg(IEnumerable<string> lazPaths, string fallbackEpsg, Action<string>? log = null)
    {
        var found = lazPaths
            .Select(DetectSourceEpsg)
            .Where(e => e != null)
            .Select(e => e!)
            .Distinct()
            .ToList();

        if (found.Count == 0)
        {
            log?.Invoke($"\nNo spatial reference stored in the point cloud — assuming EPSG:{fallbackEpsg}.");
            return fallbackEpsg;
        }
        if (found.Count > 1)
            throw new InvalidOperationException(
                "Point cloud files use different coordinate systems (" +
                string.Join(", ", found.Select(e => "EPSG:" + e)) + "). Process them separately.");

        log?.Invoke($"\nSource CRS from file: EPSG:{found[0]}");
        return found[0];
    }

    private void RunPdal(string pipelinePath)
    {
        var (exit, _, stderr) = RunPdalCapture($"pipeline \"{pipelinePath}\"",
            Path.GetDirectoryName(pipelinePath));
        if (exit != 0)
            throw new InvalidOperationException($"PDAL failed: {stderr}");
    }

    // Reads stdout and stderr concurrently so large output can't fill a pipe and hang PDAL.
    private static (int exitCode, string stdout, string stderr) RunPdalCapture(
        string arguments, string? workingDir = null)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(PdalExe, arguments)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDir ?? Path.GetTempPath()
        };
        psi.EnvironmentVariables["PROJ_LIB"] = ProjData;
        psi.EnvironmentVariables["PROJ_DATA"] = ProjData;
        psi.EnvironmentVariables["GDAL_DATA"] = GdalData;

        using var proc = System.Diagnostics.Process.Start(psi)!;
        var errTask = proc.StandardError.ReadToEndAsync();
        var stdout = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        return (proc.ExitCode, stdout, errTask.Result);
    }
}
