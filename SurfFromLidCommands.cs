using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

public class SurfFromLidCommands
{
    [CommandMethod("SURFUSERLID")]
    public void SurfaceFromLaz()
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

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var boundary = ClipBoundary.PromptSelect(ed, tr);
                if (boundary == null) return;
                if (!ClipBoundaryValidation.CheckSize(ed, boundary)) return;

                var pfr = ed.GetFileNameForOpen("Select LAZ file");
                if (pfr.Status != PromptStatus.OK) return;

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


                var extractor = new LazGroundExtractor();
                var sourceEpsg = extractor.ResolveSourceEpsg(
                    new[] { pfr.StringResult }, "6344", s => ed.WriteMessage(s));
                var targetEpsg = CivilCoordSystem.GetActiveEpsg().ToString();
                var outDir = Path.Combine(Path.GetDirectoryName(doc.Name)!, "SurfaceMgr_Output");

                var civilDoc = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
                var styleId = civilDoc.Styles.SurfaceStyles[0];

                if (pkr.StringResult == "Points")
                {
                    var enzPath = extractor.ExtractGroundPoints(
                        pfr.StringResult, boundary, targetEpsg, outDir, sourceEpsg: sourceEpsg);

                    var surface = SurfaceFactory.CreateNamedSurface(tr, "Surface_FromLAZ_Points", styleId);

                    var formats = Autodesk.Civil.DatabaseServices.PointFileFormatCollection
                        .GetPointFileFormats(db);
                    var formatId = formats["ENZ (comma delimited)"];

                    surface.PointFilesDefinition.AddPointFile(enzPath, formatId);
                    SurfaceStats.Print(ed, surface);
                }
                else
                {
                    var tifPath = extractor.DemFromGroundPoints(
                        pfr.StringResult, boundary, targetEpsg, outDir, resolution, sourceEpsg: sourceEpsg);

                    var surface = SurfaceFactory.CreateNamedSurface(tr, "Surface_FromLAZ_Raster", styleId);
                    surface.DEMFilesDefinition.AddDEMFile(tifPath);
                    SurfaceStats.Print(ed, surface);
                }

                tr.Commit();
            }

            ed.WriteMessage("\nSurface created from LAZ file.\n");
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\nSURFUSERLID failed: {ex.GetType().Name}: {ex.Message}");
            ed.WriteMessage($"\n{ex.StackTrace}");
        }
    }
}