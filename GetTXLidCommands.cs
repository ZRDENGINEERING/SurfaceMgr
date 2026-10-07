using System.Net.Http;
using System.IO;
using System.IO.Compression;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

public class GetTXLidCommands
{
    [CommandMethod("SURFTXLID")]
    public async void GetTXLid()
    {
        var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
        var db = doc.Database;
        var ed = doc.Editor;

        try
        {
            if (doc.IsNamedDrawing == false)
            {
                ed.WriteMessage("\nPlease save the drawing before running this command.");
                return;
            }

            ClipBoundary? boundary;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                boundary = ClipBoundary.PromptSelect(ed, tr);
                tr.Commit();
            }
            if (boundary == null) return;
            if (!ClipBoundaryValidation.CheckSize(ed, boundary)) return;

            // All prompts happen before the first await.
            var pko = new PromptKeywordOptions("\nSurface method: ");
            pko.Keywords.Add("Points");
            pko.Keywords.Add("Raster");
            pko.Keywords.Default = "Points";
            var pkr = ed.GetKeywords(pko);
            if (pkr.Status != PromptStatus.OK) return;

            double resolution = 0;
            if (pkr.StringResult == "Raster")
            {
                var pdr = ed.GetDouble("\nRaster cell resolution (drawing units): ");
                if (pdr.Status != PromptStatus.OK) return;
                resolution = pdr.Value;
            }

            var targetEpsg = CivilCoordSystem.GetActiveEpsg();
            var centroid = boundary.GetCentroid();

            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromMinutes(30); // LAZ tiles can be large

            ed.WriteMessage("\nFetching coordinate system definition...");
            var toWgs84 = await CoordTransform.ToWgs84Async(http, targetEpsg);
            double[] lonLat = toWgs84.MathTransform.Transform(new[] { centroid.X, centroid.Y });

            ed.WriteMessage("\nLooking up LiDAR collection...");
            var indexLocator = new TxLidarIndexLocator(http);
            var collTiles = await indexLocator.FindTilesByPointAsync(lonLat[0], lonLat[1]);
            var best = collTiles.FirstOrDefault(t => t.BestAvail == "Yes") ?? collTiles.FirstOrDefault();
            if (best == null) { ed.WriteMessage("\nNo TxGIO coverage found for this boundary."); return; }
            ed.WriteMessage($"\nCollection: {best.CollName} ({best.Year})");

            ed.WriteMessage("\nFinding qquad tiles covering boundary...");
            var gridLocator = new TxQuadGridLocator(http);
            var neededTiles = await gridLocator.FindTilesByBoundaryAsync(boundary, toWgs84);
            if (neededTiles.Count == 0) { ed.WriteMessage("\nNo qquad tiles found intersecting this boundary."); return; }
            ed.WriteMessage($"\nBoundary touches {neededTiles.Count} qquad tile(s): " +
                            string.Join(", ", neededTiles.Select(t => $"{t.DoqqNum}{t.Quadrant}")));

            var s3 = new S3CollectionLocator(http);
            var allKeys = await s3.ListCollectionResourcesAsync(best.CollId);

            // Point-cloud zips end in _lpc.zip or _sm-lpc.zip (not the _dem.zip rasters).
            var wantedNums = neededTiles.Select(t => t.DoqqNum).ToHashSet();
            var lazUrls = allKeys
                .Where(k => k.EndsWith("_lpc.zip", StringComparison.OrdinalIgnoreCase))
                .Where(k => wantedNums.Any(n => k.Contains($"_{n}_")))
                .Select(k => $"https://s3.amazonaws.com/data.tnris.org/{k}")
                .Distinct()
                .ToList();

            if (lazUrls.Count == 0) { ed.WriteMessage("\nNo matching LiDAR tiles found in this collection."); return; }
            if (lazUrls.Count < neededTiles.Count)
                ed.WriteMessage($"\nWarning: found {lazUrls.Count}/{neededTiles.Count} expected tiles.");

            var outDir = Path.Combine(Path.GetDirectoryName(doc.Name)!, "SurfaceMgr_Output");
            Directory.CreateDirectory(outDir);

            var lazPaths = new List<string>();
            foreach (var url in lazUrls)
            {
                var zipPath = Path.Combine(outDir, Path.GetFileName(url));
                var extractDir = Path.Combine(outDir, Path.GetFileNameWithoutExtension(zipPath));

                if (!Directory.Exists(extractDir) ||
                    Directory.GetFiles(extractDir, "*.laz", SearchOption.AllDirectories).Length == 0)
                {
                    ed.WriteMessage($"\nDownloading {Path.GetFileName(url)}...");
                    // Stream to disk; tiles can be hundreds of MB.
                    using (var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                    {
                        resp.EnsureSuccessStatusCode();
                        await using var src = await resp.Content.ReadAsStreamAsync();
                        await using var dst = File.Create(zipPath);
                        await src.CopyToAsync(dst);
                    }
                    ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);
                    File.Delete(zipPath);
                }

                // A zip may hold several sub-tiles; use all of them.
                lazPaths.AddRange(Directory.GetFiles(extractDir, "*.laz", SearchOption.AllDirectories));
            }

            if (lazPaths.Count == 0) { ed.WriteMessage("\nNo .laz files extracted."); return; }
            ed.WriteMessage($"\nProcessing {lazPaths.Count} LAZ file(s) (ground points only)...");

            // NOTE: TxGIO LAZ source CRS is assumed to be EPSG:6344 (NAD83(2011) UTM 15N).
            // Collections elsewhere in Texas (UTM 14N etc.) need the per-collection CRS.
            var extractor = new LazGroundExtractor();
            string? enzPath = null;
            string? tifPath = null;
            if (pkr.StringResult == "Points")
                enzPath = extractor.ExtractGroundPointsMulti(
                    lazPaths, boundary, targetEpsg.ToString(), outDir, sourceEpsg: "6344");
            else
                tifPath = extractor.DemFromGroundPointsMulti(
                    lazPaths, boundary, targetEpsg.ToString(), outDir, resolution, sourceEpsg: "6344");

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var civilDoc = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
                var styleId = civilDoc.Styles.SurfaceStyles[0];

                if (enzPath != null)
                {
                    var surface = SurfaceFactory.CreateNamedSurface(tr, "Surface_FromTXLAZ_Points", styleId);
                    var formats = Autodesk.Civil.DatabaseServices.PointFileFormatCollection.GetPointFileFormats(db);
                    surface.PointFilesDefinition.AddPointFile(enzPath, formats["ENZ (comma delimited)"]);
                    SurfaceStats.Print(ed, surface);
                }
                else
                {
                    var surface = SurfaceFactory.CreateNamedSurface(tr, "Surface_FromTXLAZ_Raster", styleId);
                    surface.DEMFilesDefinition.AddDEMFile(tifPath!);
                    SurfaceStats.Print(ed, surface);
                }
                tr.Commit();
            }

            ed.WriteMessage("\nSurface created from TX LiDAR.\n");
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\nSURFTXLID failed: {ex.GetType().Name}: {ex.Message}");
            ed.WriteMessage($"\n{ex.StackTrace}");
        }
    }
}