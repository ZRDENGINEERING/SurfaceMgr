using System.Net.Http;
using System.IO;
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
            http.Timeout = TimeSpan.FromMinutes(10); // LAZ tiles can be 1GB+

            ed.WriteMessage("\nFetching coordinate system definition...");
            var resolver = await QuadResolver.CreateAsync(http, targetEpsg);

            ed.WriteMessage("\nResolving quad...");
            var quad = await resolver.ResolveByPointAsync(centroid.X, centroid.Y);

            if (quad == null) { ed.WriteMessage("\nCouldn't resolve a quad for this boundary."); return; }
            ed.WriteMessage($"\nResolved to quad: {quad.QuadName} ({quad.QuadNum})");

            var locator = new TxgioDemLocator(http);
            var lazResources = await locator.FindQuadLazAsync(quad.QuadName,
                new Progress<string>(s => ed.WriteMessage($"\n{s}")));

            if (lazResources.Count == 0) { ed.WriteMessage("\nNo LiDAR resources found for this quad."); return; }
            if (lazResources.Count < 4)
                ed.WriteMessage($"\nWarning: only found {lazResources.Count}/4 quadrant tiles — boundary may sit near a quad edge.");

            var outDir = Path.Combine(Path.GetDirectoryName(doc.Name)!, "SurfaceMgr_Output");
            Directory.CreateDirectory(outDir);

            var lazPaths = new List<string>();
            foreach (var res in lazResources)
            {
                var zipPath = Path.Combine(outDir, Path.GetFileName(res.ResourceUrl));
                var bytes = await http.GetByteArrayAsync(res.ResourceUrl);
                await File.WriteAllBytesAsync(zipPath, bytes);

                var extractDir = Path.Combine(outDir, Path.GetFileNameWithoutExtension(zipPath));
                System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);

                var laz = Directory.GetFiles(extractDir, "*.laz", SearchOption.AllDirectories).FirstOrDefault();
                if (laz != null) lazPaths.Add(laz);
            }

            if (lazPaths.Count == 0) { ed.WriteMessage("\nNo .laz files extracted."); return; }

            var extractor = new LazGroundExtractor();

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var civilDoc = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
                var styleId = civilDoc.Styles.SurfaceStyles[0];

                if (pkr.StringResult == "Points")
                {
                    var enzPath = extractor.ExtractGroundPointsMulti(
                        lazPaths, boundary, targetEpsg.ToString(), outDir, sourceEpsg: "6344");

                    var surface = SurfaceFactory.CreateNamedSurface(tr, "Surface_FromQuadLAZ_Points", styleId);

                    var formats = Autodesk.Civil.DatabaseServices.PointFileFormatCollection
                        .GetPointFileFormats(db);
                    var formatId = formats["ENZ (comma delimited)"];

                    surface.PointFilesDefinition.AddPointFile(enzPath, formatId);
                    SurfaceStats.Print(ed, surface);
                }
                else
                {
                    var tifPath = extractor.DemFromGroundPointsMulti(
                        lazPaths, boundary, targetEpsg.ToString(), outDir, resolution, sourceEpsg: "6344");

                    var surface = SurfaceFactory.CreateNamedSurface(tr, "Surface_FromQuadLAZ_Raster", styleId);
                    surface.DEMFilesDefinition.AddDEMFile(tifPath);
                    SurfaceStats.Print(ed, surface);
                }

                tr.Commit();
            }

            ed.WriteMessage("\nSurface created from quad LAZ.\n");
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\nSURFTXLID failed: {ex.GetType().Name}: {ex.Message}");
            ed.WriteMessage($"\n{ex.StackTrace}");
        }
    }
}