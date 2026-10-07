using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using ProjNet.CoordinateSystems.Transformations;

public class ClipBoundary
{
    public List<Point2d> Vertices { get; }

    private ClipBoundary(List<Point2d> vertices) => Vertices = vertices;

    public static ClipBoundary? PromptSelect(Editor ed, Transaction tr)
    {
        var peo = new PromptEntityOptions("\nSelect closed boundary polyline: ");
        peo.SetRejectMessage("\nMust be a polyline.");
        peo.AddAllowedClass(typeof(Polyline), exactMatch: false);

        var per = ed.GetEntity(peo);
        if (per.Status != PromptStatus.OK) return null;

        var pline = tr.GetObject(per.ObjectId, OpenMode.ForRead) as Polyline;
        if (pline == null || !pline.Closed)
        {
            ed.WriteMessage("\nSelected polyline is not closed.");
            return null;
        }

        var verts = new List<Point2d>();
        for (int i = 0; i < pline.NumberOfVertices; i++)
            verts.Add(pline.GetPoint2dAt(i));
        verts.Add(verts[0]); // close the ring

        return new ClipBoundary(verts);
    }

    /// Coarse bounding rectangle — for external APIs with vertex limits
    /// (e.g. TNM's ~23-vertex cap). Never used for the actual data clip.
    public List<Point2d> GetBoundingRectangle()
    {
        double minX = Vertices.Min(v => v.X), maxX = Vertices.Max(v => v.X);
        double minY = Vertices.Min(v => v.Y), maxY = Vertices.Max(v => v.Y);

        return new List<Point2d>
        {
            new(minX, minY), new(maxX, minY),
            new(maxX, maxY), new(minX, maxY),
            new(minX, minY) // close the ring
        };
    }

    public string ToWkt()
    {
        var coords = string.Join(", ", Vertices.Select(v =>
            $"{v.X.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
            $"{v.Y.ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
        return $"POLYGON(({coords}))";
    }

    public string ToGeoJson()
    {
        var coords = string.Join(",", Vertices.Select(v =>
            $"[{v.X.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
            $"{v.Y.ToString(System.Globalization.CultureInfo.InvariantCulture)}]"));

        return $$"""
        {
          "type": "FeatureCollection",
          "features": [{
            "type": "Feature",
            "properties": {},
            "geometry": { "type": "Polygon", "coordinates": [[{{coords}}]] }
          }]
        }
        """;
    }


    public Point2d GetCentroid()
    {
        double signedArea = 0, cx = 0, cy = 0;
        int n = Vertices.Count - 1; // last vertex duplicates the first

        for (int i = 0; i < n; i++)
        {
            var p1 = Vertices[i];
            var p2 = Vertices[i + 1];
            double cross = p1.X * p2.Y - p2.X * p1.Y;
            signedArea += cross;
            cx += (p1.X + p2.X) * cross;
            cy += (p1.Y + p2.Y) * cross;
        }

        signedArea *= 0.5;
        cx /= (6.0 * signedArea);
        cy /= (6.0 * signedArea);

        return new Point2d(cx, cy);
    }



    public double GetAreaSqFt()
    {
        double area = 0;
        int n = Vertices.Count - 1; // last vertex duplicates the first to close the ring
        for (int i = 0; i < n; i++)
        {
            var p1 = Vertices[i];
            var p2 = Vertices[i + 1];
            area += (p1.X * p2.Y) - (p2.X * p1.Y);
        }
        return Math.Abs(area) / 2.0;
    }

    public double GetAreaAcres() => GetAreaSqFt() / 43560.0;


    public (double west, double south, double east, double north) ToWgs84BBox(
    ICoordinateTransformation toWgs84)
    {
        var lonLats = Vertices.Select(v => toWgs84.MathTransform.Transform(new[] { v.X, v.Y }));
        return (
            west: lonLats.Min(p => p[0]), south: lonLats.Min(p => p[1]),
            east: lonLats.Max(p => p[0]), north: lonLats.Max(p => p[1])
        );
    }
}