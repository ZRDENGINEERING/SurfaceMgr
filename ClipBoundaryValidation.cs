using Autodesk.AutoCAD.EditorInput;

public static class ClipBoundaryValidation
{
    private const double MaxAcres = 2000; // TODO: tune to whatever's actually reasonable for your work

    public static bool CheckSize(Editor ed, ClipBoundary boundary)
    {
        var acres = boundary.GetAreaAcres();
        if (acres <= MaxAcres) return true;

        ed.WriteMessage(
            $"\nBoundary is {acres:N0} acres, which exceeds the {MaxAcres:N0}-acre limit for this command. " +
            "\nTry again with a smaller boundary.");
        return false;
    }
}