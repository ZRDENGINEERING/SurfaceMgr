using System.Net.Http;
using System.IO;
using System.IO.Compression;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

public class GetUsgsLazCommands
{
    [CommandMethod("SURFUSGSLID")]
    public async void SurfUsgsLid()
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

            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromMinutes(10);

            ed.WriteMessage("\nFetching coordinate system definition...");
            var toWgs84 = await CoordTransform.ToWgs84Async(http, targetEpsg);
            var bbox = boundary.ToWgs84BBox(toWgs84);

            ed.WriteMessage("\nQuerying USGS 3DEP for LAZ...");
            var locator = new UsgsTnmLocator(http);
            var products = await locator.FindElevationProductsAsync(
                bbox.west, bbox.south, bbox.east, bbox.north, prodFormats: "LAS,LAZ");

            if (products.Count == 0) { ed.WriteMessage("\nNo USGS LiDAR products found for this boundary."); return; }

            var withYear = products
                .Select(p => new
                {
                    Product = p,
                    Year = DateTime.TryParse(p.PublicationDate, out var dt) ? dt.Year : (int?)null
                })
                .Where(x => x.Year.HasValue)
                .ToList();

            var newestYears = withYear
                .Select(x => x.Year!.Value)
                .Distinct()
                .OrderByDescending(y => y)
                .Take(2)
                .ToHashSet();

            var recentProducts = withYear
                .Where(x => newestYears.Contains(x.Year!.Value))
                .Select(x => x.Product)
                .ToList();

            if (recentProducts.Count == 0)
            {
                ed.WriteMessage("\nNo products with a parseable publication date — falling back to full list.");
                recentProducts = products;
            }

            var datasets = recentProducts
                .GroupBy(p => p.Title)
                .Select(g => new
                {
                    Title = g.Key,
                    Products = g.ToList(),
                    Year = DateTime.Parse(g.First().PublicationDate).Year
                })
                .OrderByDescending(d => d.Year)
                .ToList();

            ed.WriteMessage($"\nAvailable datasets ({newestYears.Count} most recent year(s)):");
            for (int i = 0; i < datasets.Count; i++)
                ed.WriteMessage($"\n  [{i + 1}] {datasets[i].Title} — {datasets[i].Year} ({datasets[i].Products.Count} tile(s))");

            var pir = ed.GetInteger("\nSelect dataset number: ");
            if (pir.Status != PromptStatus.OK) return;
            var chosenDataset = datasets[pir.Value - 1];

            var outDir = Path.Combine(Path.GetDirectoryName(doc.Name)!, "SurfaceMgr_Output");
            Directory.CreateDirectory(outDir);

            var lazPaths = new List<string>();
            foreach (var product in chosenDataset.Products)
            {
                var fileName = Path.GetFileName(product.DownloadUrl);
                var localPath = Path.Combine(outDir, fileName);
                var bytes = await http.GetByteArrayAsync(product.DownloadUrl);
                await File.WriteAllBytesAsync(localPath, bytes);

                if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    var extractDir = Path.Combine(outDir, Path.GetFileNameWithoutExtension(localPath));
                    ZipFile.ExtractToDirectory(localPath, extractDir, overwriteFiles: true);
                    var laz = Directory.GetFiles(extractDir, "*.laz", SearchOption.AllDirectories).FirstOrDefault();
                    if (laz != null) lazPaths.Add(laz);
                }
                else if (fileName.EndsWith(".laz", StringComparison.OrdinalIgnoreCase) ||
                         fileName.EndsWith(".las", StringComparison.OrdinalIgnoreCase))
                {
                    lazPaths.Add(localPath);
                }
            }

            if (lazPaths.Count == 0) { ed.WriteMessage("\nNo .laz files obtained."); return; }

            var extractor = new LazGroundExtractor();

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var civilDoc = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
                var styleId = civilDoc.Styles.SurfaceStyles[0];

                if (pkr.StringResult == "Points")
                {
                    var enzPath = extractor.ExtractGroundPointsMulti(
                        lazPaths, boundary, targetEpsg.ToString(), outDir);

                    var surface = SurfaceFactory.CreateNamedSurface(tr, "Surface_FromUSGS_Points", styleId);

                    var formats = Autodesk.Civil.DatabaseServices.PointFileFormatCollection
                        .GetPointFileFormats(db);
                    var formatId = formats["ENZ (comma delimited)"];

                    surface.PointFilesDefinition.AddPointFile(enzPath, formatId);
                    SurfaceStats.Print(ed, surface);
                }
                else
                {
                    var tifPath = extractor.DemFromGroundPointsMulti(
                        lazPaths, boundary, targetEpsg.ToString(), outDir, resolution);

                    var surface = SurfaceFactory.CreateNamedSurface(tr, "Surface_FromUSGS_Raster", styleId);
                    surface.DEMFilesDefinition.AddDEMFile(tifPath);
                    SurfaceStats.Print(ed, surface);
                }

                tr.Commit();
            }

            ed.WriteMessage("\nSurface created from USGS LiDAR.\n");
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\nSURFUSGSLID failed: {ex.GetType().Name}: {ex.Message}");
            ed.WriteMessage($"\n{ex.StackTrace}");
        }
    }
}