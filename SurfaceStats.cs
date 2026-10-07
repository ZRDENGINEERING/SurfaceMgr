using Autodesk.AutoCAD.EditorInput;
using Autodesk.Civil.DatabaseServices;

public static class SurfaceStats
{
    public static void Print(Editor ed, Autodesk.Civil.DatabaseServices.Surface surface)
    {
        var props = surface.GetGeneralProperties();

        ed.WriteMessage($"\n--- {surface.Name} statistics ---");
        ed.WriteMessage($"\n  Points:       {props.NumberOfPoints:N0}");
        ed.WriteMessage($"\n  X range:      {props.MinimumCoordinateX:N2} to {props.MaximumCoordinateX:N2}");
        ed.WriteMessage($"\n  Y range:      {props.MinimumCoordinateY:N2} to {props.MaximumCoordinateY:N2}");
        ed.WriteMessage($"\n  Elevation:    {props.MinimumElevation:N2} to {props.MaximumElevation:N2} " +
                         $"(mean {props.MeanElevation:N2})");
        ed.WriteMessage("\n----------------------------------\n");
    }
}