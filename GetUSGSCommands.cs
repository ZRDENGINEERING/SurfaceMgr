using System.Net.Http;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

public class GetUSGSCommands
{
    [CommandMethod("SURFUSGSDEM")]
    public async void GetUsgsDem()
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

            var targetEpsg = CivilCoordSystem.GetActiveEpsg();

            using var http = new HttpClient();
            ed.WriteMessage("\nFetching coordinate system definition...");
            var toWgs84 = await CoordTransform.ToWgs84Async(http, targetEpsg);
            var bbox = boundary.ToWgs84BBox(toWgs84);

            ed.WriteMessage("\nQuerying USGS 3DEP...");
            var locator = new UsgsTnmLocator(http);
            var products = await locator.FindElevationProductsAsync(
                bbox.west, bbox.south, bbox.east, bbox.north);

            // After fetching products from FindElevationProductsAsync...

            if (products.Count == 0) { ed.WriteMessage("\nNo USGS elevation products found for this boundary."); return; }

            // Parse publication year, keep only the 2 most recent years represented
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

            // Group by title (dataset) within the filtered years
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

            var urls = chosenDataset.Products.Select(p => p.DownloadUrl).ToList();

            var merger = new MergerDEMs();
            var outDir = Path.Combine(Path.GetDirectoryName(doc.Name)!, "SurfaceMgr_Output");
            var tifPath = await merger.DownloadMergeAndClipStreamedAsync(
                urls, "USGS_DEM", boundary, targetEpsg, outDir);

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var civilDoc = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
                var styleId = civilDoc.Styles.SurfaceStyles[0];

                var surfaceName = $"Surface_FromUSGS_{DateTime.Now:yyyyMMdd_HHmmss}";
                var surface = SurfaceFactory.CreateNamedSurface(tr, "Surface_FromUSGS_DEM", styleId);

                surface.DEMFilesDefinition.AddDEMFile(tifPath);
                SurfaceStats.Print(ed, surface);
                tr.Commit();
            }

            ed.WriteMessage("\nSurface created from USGS DEM.\n");
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\nGETUSGSDEM failed: {ex.GetType().Name}: {ex.Message}");
            ed.WriteMessage($"\n{ex.StackTrace}");
        }
    }
}