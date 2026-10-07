using System.Net.Http;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

public class GetTXDEMCommands
{
    [CommandMethod("SURFTXDEM")]
    public async void GetTXDem()
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
            var centroid = boundary.GetCentroid();

            using var http = new HttpClient();
            ed.WriteMessage("\nFetching coordinate system definition...");
            var toWgs84 = await CoordTransform.ToWgs84Async(http, targetEpsg);
            double[] lonLat = toWgs84.MathTransform.Transform(new[] { centroid.X, centroid.Y });

            ed.WriteMessage("\nLooking up LiDAR collection...");
            var indexLocator = new TxLidarIndexLocator(http);
            var collTiles = await indexLocator.FindTilesByPointAsync(lonLat[0], lonLat[1]);
            var best = collTiles.FirstOrDefault(t => t.BestAvail == "Yes") ?? collTiles.FirstOrDefault();
            if (best == null) { ed.WriteMessage("\nNo TxGIO coverage found for this boundary."); return; }

            if (!TnrisCollectionCatalog.TileHeaderByCollId.TryGetValue(best.CollId, out var slug))
            {
                ed.WriteMessage($"\nCollection '{best.CollName}' ({best.CollId}) isn't in the catalog yet — add it to TnrisCollectionCatalog.");
                return;
            }
            ed.WriteMessage($"\nCollection: {best.CollName} ({best.Year}), slug: {slug}");

            ed.WriteMessage("\nFinding qquad tiles covering boundary...");
            var gridLocator = new TxQuadGridLocator(http);
            var neededTiles = await gridLocator.FindTilesByBoundaryAsync(boundary, toWgs84);

            if (neededTiles.Count == 0) { ed.WriteMessage("\nNo qquad tiles found intersecting this boundary."); return; }
            ed.WriteMessage($"\nBoundary touches {neededTiles.Count} qquad tile(s): " +
                             string.Join(", ", neededTiles.Select(t => $"{t.DoqqNum}{t.Quadrant}")));

            var s3 = new S3CollectionLocator(http);
            var allKeys = await s3.ListCollectionResourcesAsync(best.CollId);

            var wantedNums = neededTiles.Select(t => t.DoqqNum).ToHashSet();
            var demUrls = allKeys
                .Where(k => k.EndsWith("_dem.zip", StringComparison.OrdinalIgnoreCase))
                .Where(k => wantedNums.Any(n => k.Contains($"_{n}_")))
                .Select(k => $"https://s3.amazonaws.com/data.tnris.org/{k}")
                .ToList();

            if (demUrls.Count == 0) { ed.WriteMessage("\nNo matching DEM tiles found in this collection."); return; }
            if (demUrls.Count < neededTiles.Count)
                ed.WriteMessage($"\nWarning: found {demUrls.Count}/{neededTiles.Count} expected tiles.");

            var merger = new MergerDEMs();
            var outDir = Path.Combine(Path.GetDirectoryName(doc.Name)!, "SurfaceMgr_Output");
            var tifPath = await merger.DownloadMergeAndClipAsync(
                demUrls.Select(u => new TxgioResource { ResourceUrl = u }).ToList(),
                slug, boundary, targetEpsg, outDir);

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var civilDoc = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
                var styleId = civilDoc.Styles.SurfaceStyles[0];

                var surface = SurfaceFactory.CreateNamedSurface(tr, "Surface_FromTX", styleId);
                surface.DEMFilesDefinition.AddDEMFile(tifPath);
                SurfaceStats.Print(ed, surface);
                tr.Commit();
            }

            ed.WriteMessage("\nSurface created from TX DEM.\n");
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\nSURFTXDEM failed: {ex.GetType().Name}: {ex.Message}");
            ed.WriteMessage($"\n{ex.StackTrace}");
        }
    }
}