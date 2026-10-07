using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.DatabaseServices;

public static class SurfaceFactory
{
    public static TinSurface CreateNamedSurface(Transaction tr, string baseName, ObjectId styleId)
    {
        var surfaceName = $"{baseName}_{DateTime.Now:yyyyMMdd_HHmmss}";
        var surfaceId = TinSurface.Create(surfaceName, styleId);
        var surface = tr.GetObject(surfaceId, OpenMode.ForWrite) as TinSurface;

        if (surface == null)
            throw new InvalidOperationException($"Failed to open newly created surface '{surfaceName}' for write.");

        return surface;
    }
}