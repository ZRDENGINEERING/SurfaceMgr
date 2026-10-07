public static class CivilCoordSystem
{
    private static readonly Dictionary<string, int> KnownZones = new()
    {
        ["TX83-NF"] = 2275, // Texas North
        ["TX83-NCF"] = 2276, // Texas North Central
        ["TX83-CF"] = 2277, // Texas Central
        ["TX83-SCF"] = 2278, // Texas South Central
        ["TX83-SF"] = 2279, // Texas South
    };

    public static int GetActiveEpsg()
    {
        var civilDoc = Autodesk.Civil.ApplicationServices.CivilApplication.ActiveDocument;
        var code = civilDoc.Settings.DrawingSettings.UnitZoneSettings.CoordinateSystemCode;

        if (string.IsNullOrEmpty(code))
            throw new InvalidOperationException(
                "No coordinate system is assigned to this drawing (DRAWINGSETTINGS). " +
                "Set one before running this command.");

        if (!KnownZones.TryGetValue(code, out var epsg))
            throw new InvalidOperationException(
                $"Coordinate system '{code}' isn't in the known-zones table yet — add it.");

        return epsg;
    }
}
