namespace AAEmu.Game.Models.Game.World;

public class Point
{
    public uint WorldId { get; set; }
    public uint ZoneId { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public sbyte RotationX { get; set; }
    public sbyte RotationY { get; set; }
    public sbyte RotationZ { get; set; }

    public Point()
    {
    }

    public Point(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public Point(float x, float y, float z, sbyte rotationX, sbyte rotationY, sbyte rotationZ)
    {
        X = x;
        Y = y;
        Z = z;
        RotationX = rotationX;
        RotationY = rotationY;
        RotationZ = rotationZ;
    }

    public Point(uint worldId, uint zoneId, float x, float y, float z, sbyte rotationX, sbyte rotationY, sbyte rotationZ)
    {
        WorldId = worldId;
        ZoneId = zoneId;
        X = x;
        Y = y;
        Z = z;
        RotationX = rotationX;
        RotationY = rotationY;
        RotationZ = rotationZ;
    }

    public Point Clone()
    {
        return new Point(WorldId, ZoneId, X, Y, Z, RotationX, RotationY, RotationZ);
    }

    internal static bool OnSegment(Point p, Point q, Point r)
    {
        if (q.X <= Math.Max(p.X, r.X) &&
            q.X >= Math.Min(p.X, r.X) &&
            q.Y <= Math.Max(p.Y, r.Y) &&
            q.Y >= Math.Min(p.Y, r.Y))
        {
            return true;
        }
        return false;
    }

    // To find orientation of ordered triplet (p, q, r).
    // The function returns following values
    // 0 --> p, q and r are colinear
    // 1 --> Clockwise
    // 2 --> Counterclockwise
    internal static int FindTripletOrientation(Point p, Point q, Point r)
    {
        var val = (q.Y - p.Y) * (r.X - q.X) -
                  (q.X - p.X) * (r.Y - q.Y);

        if (val == 0.0f)
        {
            return 0; // colinear
        }
        return val > 0 ? 1 : 2; // clock or counterclock wise
    }

    // The function that returns true if
    // line segment 'p1q1' and 'p2q2' intersect.
    internal static bool IsLineIntersection(
        (Point p, Point q) line1,
        (Point p, Point q) line2)
    {
        // Find the four orientations needed for
        // general and special cases
        var o1 = FindTripletOrientation(line1.p, line1.q, line2.p);
        var o2 = FindTripletOrientation(line1.p, line1.q, line2.q);
        var o3 = FindTripletOrientation(line2.p, line2.q, line1.p);
        var o4 = FindTripletOrientation(line2.p, line2.q, line1.q);

        // General case
        if (o1 != o2 && o3 != o4)
        {
            return true;
        }

        // Special Cases
        // p1, q1 and p2 are colinear and
        // p2 lies on segment p1q1
        if (o1 == 0 && OnSegment(line1.p, line2.p, line1.q))
        {
            return true;
        }

        // p1, q1 and p2 are colinear and
        // q2 lies on segment p1q1
        if (o2 == 0 && OnSegment(line1.p, line2.q, line1.q))
        {
            return true;
        }

        // p2, q2 and p1 are colinear and
        // p1 lies on segment p2q2
        if (o3 == 0 && OnSegment(line2.p, line1.p, line2.q))
        {
            return true;
        }

        // p2, q2 and q1 are colinear and
        // q1 lies on segment p2q2
        if (o4 == 0 && OnSegment(line2.p, line1.q, line2.q))
        {
            return true;
        }

        // Doesn't fall in any of the above cases
        return false;
    }

    // Returns true if the point p lies inside the polygon[] with n vertices, or on one of its edges.
    // Even-odd crossing test along a ray towards +X. Each edge is taken as half-open in Y, so a ray
    // through a vertex counts the two edges meeting there once between them, and a horizontal edge
    // never counts. Positions here sit on a grid, so a ray through a vertex is the common case rather
    // than a rare one.
    public static bool IsInside(IReadOnlyList<Point> polygon, int n, Point p)
    {
        // There must be at least 3 vertices in polygon[]
        if (n < 3)
        {
            return false;
        }

        var inside = false;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            var a = polygon[i];
            var b = polygon[j];

            if (FindTripletOrientation(a, p, b) == 0 && OnSegment(a, p, b))
            {
                return true;
            }

            if ((a.Y > p.Y) != (b.Y > p.Y) &&
                p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }
}
