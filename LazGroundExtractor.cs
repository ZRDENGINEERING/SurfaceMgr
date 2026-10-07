using System.IO;
using System.Text.Json;

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

    private void RunPdal(string pipelinePath)
    {
        const string pdalExe = @"C:\OSGeo4W\bin\pdal.exe";
        const string projData = @"C:\OSGeo4W\share\proj";
        const string gdalData = @"C:\OSGeo4W\share\gdal";

        var psi = new System.Diagnostics.ProcessStartInfo(pdalExe, $"pipeline \"{pipelinePath}\"")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(pipelinePath) ?? Path.GetTempPath()
        };
        psi.EnvironmentVariables["PROJ_LIB"] = projData;
        psi.EnvironmentVariables["PROJ_DATA"] = projData;
        psi.EnvironmentVariables["GDAL_DATA"] = gdalData;

        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.WaitForExit();
        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"PDAL failed: {proc.StandardError.ReadToEnd()}");
    }
}