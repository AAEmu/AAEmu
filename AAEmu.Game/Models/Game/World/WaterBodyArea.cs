using System.Drawing;
using System.Numerics;
using AAEmu.Game.Models.Game.World.Transform;
using Newtonsoft.Json;

namespace AAEmu.Game.Models.Game.World;

public class WaterBodyArea
{
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public uint Id { get; set; }

    [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public WaterBodyAreaType AreaType { get; set; }
    
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public float Height { get; set; }

    [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public float Depth { get; set; }
    
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public string Name { get; set; }

    [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public string Guid { get; set; }

    [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public float Speed { get; set; }

    /// <summary>Unit flow axis in XY (ignored if <see cref="FlowSpeedAbs"/> is 0).</summary>
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public Vector2 FlowAxis { get; set; }

    /// <summary>Absolute flow speed (m/s). Contract: {0,1} for gameplay.</summary>
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public float FlowSpeedAbs { get; set; }

    /// <summary>Signed flow speed along <see cref="FlowAxis"/> (downhill sign). Contract: {-1,0,1}.</summary>
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public float FlowSpeedSigned { get; set; }

    [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public float RiverWidth { get; set; }

    /// <summary>Extra half-width (m) beyond <see cref="RiverWidth"/> for LineArray corridor tests (edge tolerance).</summary>
    private const float RiverCorridorEdgePaddingMeters = 1f;

    [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public List<Vector3> Points { get; set; }

    [JsonIgnore]
    public List<Vector3> BorderPoints { get; set; }
    [JsonIgnore]
    public RectangleF _boundingBox = RectangleF.Empty;
    [JsonIgnore]
    public float _lowest;
    [JsonIgnore]
    public float _highest;

    [JsonIgnore]
    public RectangleF BoundingBox { get => _boundingBox; }

    [JsonIgnore]
    public float Highest { get => _highest; }
    
    public WaterBodyArea()
    {
        AreaType = WaterBodyAreaType.Polygon;
        Points = [];
        BorderPoints = [];
        FlowAxis = Vector2.UnitX;
    }

    public WaterBodyArea(string name, WaterBodyAreaType areaType)
    {
        AreaType = areaType;
        Name = name;
        Points = [];
        BorderPoints = [];
        FlowAxis = Vector2.UnitX;
    }

    /// <summary>
    /// Checks if a point is inside the water body, and returns a surface points of the given location
    /// </summary>
    /// <param name="point">Point to check against</param>
    /// <param name="surfacePoint">Surface point of the water at the X,Y location provided by point</param>
    /// <param name="flowDirection">In case the water body is a LineArray, this returns the water-flow direction</param>
    /// <returns>Returns true if point is within the water area</returns>
    public bool GetSurface(Vector3 point, out Vector3 surfacePoint, out Vector3 flowDirection)
    {
        if (!Contains(point.X, point.Y, out flowDirection))
        {
            surfacePoint = Vector3.Zero;
            return false;
        }
        
        if (AreaType == WaterBodyAreaType.Polygon)
        {
            // For flat areas, just use the top
            surfacePoint = new Vector3(point.X, point.Y, _highest);
        }
        else
        if (AreaType == WaterBodyAreaType.LineArray)
        {
            // For flowing water find the actual closest point, and use its position as height
            var closestPoint = Points[0];
            var closestDistance = 1000000000f;
            for (var i = 0; i < Points.Count; i++)
            {
                // Using Length² as it's faster, and we don't care about the actual distance other than finding the closest
                var thisDistance = (point - Points[i]).LengthSquared();
                if (thisDistance < closestDistance)
                {
                    closestDistance = thisDistance;
                    closestPoint = Points[i];
                }
            }
            surfacePoint = new Vector3(point.X, point.Y, closestPoint.Z);
        }
        else
        {
            // Fallback that shouldn't happen
            surfacePoint = new Vector3(point.X, point.Y, _highest);
        }
        return true;
    }

    public void UpdateBounds()
    {
        _highest = 0f;
        _lowest = 0f;
        var first = true;

        var xMin = 0f;
        var yMin = 0f;
        var xMax = 0f;
        var yMax = 0f;

        if (AreaType == WaterBodyAreaType.Polygon)
        {
            // If a polygon, just copy the points
            BorderPoints = Points;
        }
        else if (AreaType == WaterBodyAreaType.LineArray)
        {
            // If a line array, generate a border
            BorderPoints = [];
            var otherSide = new List<Vector3>();
            
            var directionVector = Points.Count >= 2 ? Vector3.Normalize(Points[0] - Points[1]) : Vector3.Zero;
            // going downstream 0 -> max
            for (var side = 0; side < Points.Count - 1; side++)
            {
                directionVector = Vector3.Normalize(Points[side] - Points[side + 1]);
                
                var perpendicular = Vector3.Normalize(Vector3.Cross(directionVector, Vector3.UnitZ));
                
                var right = new PositionAndRotation(Points[side], directionVector);
                right.AddDistance(perpendicular * RiverWidth * -1f);
                BorderPoints.Add(right.Position);
                
                var left = new PositionAndRotation(Points[side], directionVector);
                left.AddDistance(perpendicular * RiverWidth);
                otherSide.Insert(0,left.Position);
            }
            BorderPoints.AddRange(otherSide);
        }
        else
        {
            BorderPoints = [];
        }
        
        var boundsPad = RiverWidth;
        if (AreaType == WaterBodyAreaType.LineArray)
            boundsPad += RiverCorridorEdgePaddingMeters;

        foreach (var point in Points)
        {
            // Just take the first point
            if (first)
            {
                first = false;
                xMin = point.X - boundsPad;
                yMin = point.Y - boundsPad;
                xMax = point.X + boundsPad;
                yMax = point.Y + boundsPad;
                
                if (AreaType == WaterBodyAreaType.Polygon)
                {
                    _lowest = point.Z;
                    _highest = _lowest;
                }

                if (AreaType == WaterBodyAreaType.LineArray)
                {
                    _highest = point.Z;
                    _lowest = _highest;
                }
            }
            else
            {
                // The RiverWidth is used to generate a very rough range of the river's width for the rough bounds check
                if (point.X - boundsPad < xMin)
                    xMin = point.X - boundsPad;
                if (point.X + boundsPad > xMax)
                    xMax = point.X + boundsPad;
                if (point.Y - boundsPad < yMin)
                    yMin = point.Y - boundsPad;
                if (point.Y + boundsPad > yMax)
                    yMax = point.Y + boundsPad;
                
                // Z
                if (point.Z > _highest)
                    _highest = point.Z;
                if (point.Z < _lowest)
                    _lowest = point.Z;
            }
        }
        _boundingBox = new RectangleF(xMin, yMin, xMax - xMin, yMax - yMin);
    }

    /// <summary>
    /// Checks if a point is inside the water body, and also returns the direction vector if it's a flowing river
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="flowVector"></param>
    /// <returns></returns>
    private bool Contains(float x, float y, out Vector3 flowVector)
    {
        flowVector = Vector3.Zero;

        if (!_boundingBox.Contains(x, y))
            return false;

        // Rivers: corridor around the centerline (2D distance to segments). The old ray+BorderPoints+angle
        // path dropped valid points (e.g. 2-point rivers had zero direction; angle checks skipped segments).
        if (AreaType == WaterBodyAreaType.LineArray)
            return ContainsRiverCorridor(x, y, out flowVector);

        var inside = PointInsidePolygon2D(x, y);
        if (!inside)
            return false;

        if (FlowSpeedAbs <= 0f || FlowSpeedSigned == 0f)
            return true;

        var axis = FlowAxis;
        var axisLenSq = axis.LengthSquared();
        if (axisLenSq <= 1e-12f)
            return true;

        var axisUnit = axis / MathF.Sqrt(axisLenSq);
        flowVector = new Vector3(axisUnit.X, axisUnit.Y, 0f) * FlowSpeedSigned;
        return true;
    }

    /// <summary>Odd-even horizontal ray along +X (stable for lakes vs diagonal ray from bbox corner).</summary>
    private bool PointInsidePolygon2D(float x, float y)
    {
        if (BorderPoints == null || BorderPoints.Count < 3)
            return false;

        var inside = false;
        for (var i = 0; i + 1 < BorderPoints.Count; i++)
        {
            var yi = BorderPoints[i].Y;
            var yj = BorderPoints[i + 1].Y;
            if ((yi > y) == (yj > y))
                continue;

            var xi = BorderPoints[i].X;
            var xj = BorderPoints[i + 1].X;
            var denom = yj - yi;
            if (MathF.Abs(denom) < 1e-20f)
                continue;

            var xinters = xi + (y - yi) / denom * (xj - xi);
            if (x < xinters)
                inside = !inside;
        }

        return inside;
    }

    private bool ContainsRiverCorridor(float x, float y, out Vector3 flowVector)
    {
        flowVector = Vector3.Zero;
        if (Points.Count < 2)
            return false;

        var p = new Vector3(x, y, _highest);
        var bestDist = float.PositiveInfinity;
        var bestFlow = Vector3.Zero;
        var halfWidth = RiverWidth + RiverCorridorEdgePaddingMeters;

        for (var side = 0; side < Points.Count - 1; side++)
        {
            var d = MinimumDistanceToLine(Points[side], Points[side + 1], p);
            if (d >= bestDist)
                continue;
            bestDist = d;
            var seg = Points[side + 1] - Points[side];
            bestFlow = seg.LengthSquared() > 1e-12f ? Vector3.Normalize(seg) * FlowSpeedSigned : Vector3.Zero;
        }

        if (bestDist > halfWidth)
            return false;

        flowVector = bestFlow;
        return true;
    }

    public Vector3 GetCenter(bool atSurface)
    {
        var h = atSurface ? _highest : _lowest - Depth;
        return new Vector3(_boundingBox.Left + _boundingBox.Width / 2f, _boundingBox.Top + _boundingBox.Height / 2f, h);
    }
    
    // https://stackoverflow.com/questions/849211/shortest-distance-between-a-point-and-a-line-segment
    /// <summary>
    /// Get minimum distance of P3 to line V3-W3, in 2D space (only using X and Y)
    /// </summary>
    /// <param name="v3"></param>
    /// <param name="w3"></param>
    /// <param name="p3"></param>
    /// <returns>Distance</returns>
    public float MinimumDistanceToLine(Vector3 v3, Vector3 w3, Vector3 p3)
    {
        var v = new Vector2(v3.X, v3.Y);
        var w = new Vector2(w3.X, w3.Y);
        var p = new Vector2(p3.X, p3.Y);
        // Return minimum distance between line segment vw and point p
        var l2 = (w - v).LengthSquared();  // i.e. |w-v|^2 -  avoid a sqrt
        if (l2 == 0f) return (v - p).Length(); // v == w case, should not happen in our case
        
        // Consider the line extending the segment, parameterized as v + t (w - v).
        // We find projection of point p onto the line.
        // We clamp t from [0,1] to handle points outside the segment vw.
        
        var t = MathF.Max(0, MathF.Min(1,  Vector2.Dot(p - v, w - v) / l2));
        var projection = v + t * (w - v);  // Projection falls on the segment
        
        return (projection - p).Length();
    }
}
