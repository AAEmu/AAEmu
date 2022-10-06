using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Utils;
using Newtonsoft.Json;
using SQLitePCL;

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
    public int WaterType { get; set; }
    
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public float Speed { get; set; }

    [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public float RiverWidth { get; set; }
    
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
        Points = new List<Vector3>();
        BorderPoints = new List<Vector3>();
    }

    public WaterBodyArea(string name, WaterBodyAreaType areaType)
    {
        AreaType = areaType;
        Name = name;
        Points = new List<Vector3>();
        BorderPoints = new List<Vector3>();
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
            // For flowing water find the actual closest point, and use it's position as height
            var closestPoint = Points[0];
            var closestDistance = 1000000000f;
            for (var i = 0; i < Points.Count - 1; i++)
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
            BorderPoints = new List<Vector3>();
            var OtherSide = new List<Vector3>();
            
            var directionVector = Vector3.Zero;
            // going downstream 0 -> max
            for (var side = 0; side < Points.Count-1; side++)
            {
                if (side < Points.Count - 2)
                    directionVector = Vector3.Normalize(Points[side] - Points[side + 1]);
                
                var perpendicular = Vector3.Normalize(Vector3.Cross(directionVector, Vector3.UnitZ));
                
                var right = new PositionAndRotation(Points[side], directionVector);
                right.AddDistance(perpendicular * RiverWidth * -1f);
                BorderPoints.Add(right.Position);
                
                var left = new PositionAndRotation(Points[side], directionVector);
                left.AddDistance(perpendicular * RiverWidth);
                OtherSide.Insert(0,left.Position);
            }
            BorderPoints.AddRange(OtherSide);
        }
        else
        {
            BorderPoints = new List<Vector3>();
        }
        
        foreach (var point in Points)
        {
            // Just take the first point
            if (first)
            {
                first = false;
                xMin = point.X - RiverWidth;
                yMin = point.Y - RiverWidth;
                xMax = point.X + RiverWidth;
                yMax = point.Y + RiverWidth;
                
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
                if (point.X - RiverWidth < xMin)
                    xMin = point.X - RiverWidth;
                if (point.X + RiverWidth > xMax)
                    xMax = point.X + RiverWidth;
                if (point.Y - RiverWidth < yMin)
                    yMin = point.Y - RiverWidth;
                if (point.Y + RiverWidth > yMax)
                    yMax = point.Y + RiverWidth;
                
                // Z
                if (point.Z > _highest)
                    _highest = point.Z;
                if (point.Z < _lowest)
                    _lowest = point.Z;
            }
        }
        _boundingBox = new RectangleF(xMin, yMin, xMax - xMin, yMax - yMin);
    }
    
    private bool AreLinesIntersecting(Vector2 v1Start, Vector2 v1End, Vector2 v2Start, Vector2 v2End)
    {
        float d1, d2;
        float a1, a2, b1, b2, c1, c2;

        // Convert vector 1 to a line (line 1) of infinite length.
        // We want the line in linear equation standard form: A*x + B*y + C = 0
        // See: http://en.wikipedia.org/wiki/Linear_equation
        a1 = v1End.Y - v1Start.Y;
        b1 = v1Start.X - v1End.X;
        c1 = (v1End.X * v1Start.Y) - (v1Start.X * v1End.Y);

        // Every point (x,y), that solves the equation above, is on the line,
        // every point that does not solve it, is not. The equation will have a
        // positive result if it is on one side of the line and a negative one 
        // if is on the other side of it. We insert (x1,y1) and (x2,y2) of vector
        // 2 into the equation above.
        d1 = (a1 * v2Start.X) + (b1 * v2Start.Y) + c1;
        d2 = (a1 * v2End.X) + (b1 * v2End.Y) + c1;

        // If d1 and d2 both have the same sign, they are both on the same side
        // of our line 1 and in that case no intersection is possible. Careful, 
        // 0 is a special case, that's why we don't test ">=" and "<=", 
        // but "<" and ">".
        if (d1 > 0 && d2 > 0) return false;
        if (d1 < 0 && d2 < 0) return false;

        // The fact that vector 2 intersected the infinite line 1 above doesn't 
        // mean it also intersects the vector 1. Vector 1 is only a subset of that
        // infinite line 1, so it may have intersected that line before the vector
        // started or after it ended. To know for sure, we have to repeat the
        // the same test the other way round. We start by calculating the 
        // infinite line 2 in linear equation standard form.
        a2 = v2End.Y - v2Start.Y;
        b2 = v2Start.X - v2End.X;
        c2 = (v2End.X * v2Start.Y) - (v2Start.X * v2End.Y);

        // Calculate d1 and d2 again, this time using points of vector 1.
        d1 = (a2 * v1Start.X) + (b2 * v1Start.Y) + c2;
        d2 = (a2 * v1End.X) + (b2 * v1End.Y) + c2;

        // Again, if both have the same sign (and neither one is 0),
        // no intersection is possible.
        if (d1 > 0 && d2 > 0) return false;
        if (d1 < 0 && d2 < 0) return false;

        // If we get here, only two possibilities are left. Either the two
        // vectors intersect in exactly one point or they are collinear, which
        // means they intersect in any number of points from zero to infinite.
        if ((a1 * b2) - (a2 * b1) == 0.0f) return false; // COLLINEAR;

        // If they are not collinear, they must intersect in exactly one point.
        return true;
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
        // Info: https://stackoverflow.com/questions/217578/how-can-i-determine-whether-a-2d-point-is-within-a-polygon
        flowVector = Vector3.Zero; // Only river (line array) types can return a flow direction

        // Very rough test for better speed
        if (!_boundingBox.Contains(x, y))
            return false;

        // Check for Polygon type, or rough LineArray shape
        // if (AreaType == WaterBodyAreaType.Polygon)
        // Test the ray against all sides
        var intersections = 0;
        for (var side = 0; side < BorderPoints.Count - 1; side++)
        {
            var sideStart = new Vector2(BorderPoints[side].X, BorderPoints[side].Y);
            var sideEnd = new Vector2(BorderPoints[side + 1].X, BorderPoints[side + 1].Y);
            var rayStart = new Vector2(_boundingBox.X - 1f, _boundingBox.Y - 1f);
            var rayEnd = new Vector2(x, y);

            // Test if current side intersects with ray.
            // If yes, intersections++;
            if (AreLinesIntersecting(rayStart, rayEnd, sideStart, sideEnd))
                intersections++;
        }

        var res = ((intersections & 1) == 1);

        // Check for Area Types
        if (res && (AreaType == WaterBodyAreaType.LineArray))
        {
            var p = new Vector3(x, y, _highest);
            var closestPoint = -1;
            var closestDistance = 1000000f;

            // Test the ray against all sides
            for (var side = 0; side < Points.Count - 2; side++)
            {
                var distanceToLine = MinimumDistanceToLine(Points[side], Points[side + 1], p);

                var nextPointDirection = MathUtil.CalculateDirection(Points[side + 1], Points[side]);

                // For the first point in the line, make sure it's in front of it
                //if (side <= 0)
                {
                    var fromPointDirection = MathUtil.CalculateDirection(p, Points[side]);
                    var diff = Math.Abs(nextPointDirection - fromPointDirection) * (180 / MathF.PI);

                    if (((diff >= 90) && (diff <= 270))) // in front
                        continue;
                }

                // For the last point in the line, make sure it's behind it
                if (side >= Points.Count - 1)
                {
                    var fromPointDirection = MathUtil.CalculateDirection(p, Points[side]);
                    var diff = Math.Abs(nextPointDirection - fromPointDirection) * (180 / MathF.PI);

                    if (!((diff >= 90) && (diff <= 270))) // NOT in front
                        continue;
                }

                if ((distanceToLine < RiverWidth))
                {
                    // looks like it's roughly in range of the river

                    if (distanceToLine < closestDistance)
                    {
                        closestDistance = distanceToLine;
                        closestPoint = side;
                        flowVector = (Points[side + 1] - Points[side]);
                        flowVector = Vector3.Normalize(flowVector) * Speed;
                    }
                }
            }

            if (closestPoint < 0)
                res = false;
        }

        return res;
    }

    public Vector3 GetCenter(bool atSurface)
    {
        var h = atSurface ? _highest : _lowest - Depth;
        return new Vector3(_boundingBox.Left + (_boundingBox.Width / 2f), _boundingBox.Top + (_boundingBox.Height / 2f), h);
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
