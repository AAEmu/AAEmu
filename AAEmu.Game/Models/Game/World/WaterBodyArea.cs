using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using Newtonsoft.Json;

namespace AAEmu.Game.Models.Game.World;

public class WaterBodyArea
{
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public uint Id { get; set; }
    
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public float Height { get; set; }
    
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public string Name { get; set; }
    
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public string Guid { get; set; }

    [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public int WaterType { get; set; }
    
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public Vector3 Direction { get; set; } = Vector3.Zero;
    
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    public List<Vector3> Points { get; set; }
    
    [JsonIgnore]
    public RectangleF _boundingBox = RectangleF.Empty;
    [JsonIgnore]
    public float _lowest;
    [JsonIgnore]
    public float _heighest;

    public WaterBodyArea()
    {
        Points = new List<Vector3>();
    }

    public WaterBodyArea(string name)
    {
        Name = name;
        Points = new List<Vector3>();
    }

    public bool IsWater(Vector3 point)
    {
        // First do a check in 2D top view
        if (!Contains(point.X, point.Y))
            return false;
        
        // If it's in withing the shape, check the height (assumes shape is flat)
        // Is it above the estimated water ?
        if (point.Z > _heighest + Height)
            return false;

        // Is it below the estimated water ?
        if (point.Z < _lowest)
            return false;

        // So I guess it's in the water then
        return true;
    }

    public bool GetSurface(Vector3 point, out Vector3 surfacePoint)
    {
        if (!Contains(point.X, point.Y))
        {
            surfacePoint = Vector3.Zero;
            return false;
        }

        surfacePoint = new Vector3(point.X, point.Y,_heighest + Height);
        return true;
    }

    public void UpdateBounds()
    {
        _heighest = 0f;
        _lowest = 0f;
        var first = true;

        var xMin = 0f;
        var yMin = 0f;
        var xMax = 0f;
        var yMax = 0f;
        
        foreach (var point in Points)
        {
            // Just take the first point
            if (first)
            {
                first = false;
                xMin = point.X;
                yMin = point.Y;
                xMax = point.X;
                yMax = point.Y;
                _lowest = point.Z;
                _heighest = _lowest;
            }
            else
            {
                if (point.X < xMin)
                    xMin = point.X;
                if (point.X > xMax)
                    xMax = point.X;
                if (point.Y < yMin)
                    yMin = point.Y;
                if (point.Y > yMax)
                    yMax = point.Y;
                
                // Z
                if (point.Z > _heighest)
                    _heighest = point.Z;
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

    private bool Contains(float x, float y)
    {
        // Info: https://stackoverflow.com/questions/217578/how-can-i-determine-whether-a-2d-point-is-within-a-polygon

        // Very rough test for better speed
        if (!_boundingBox.Contains(x, y))
            return false;

        // Test the ray against all sides
        var intersections = 0;
        for (var side = 0; side < Points.Count - 1; side++)
        {
            var sideStart = new Vector2(Points[side].X, Points[side].Y);
            var sideEnd = new Vector2(Points[side + 1].X, Points[side + 1].Y);
            var rayStart = new Vector2(_boundingBox.X - 1f, _boundingBox.Y - 1f);
            var rayEnd = new Vector2(x, y);

            // Test if current side intersects with ray.
            // If yes, intersections++;
            if (AreLinesIntersecting(rayStart, rayEnd, sideStart, sideEnd))
                intersections++;
        }

        return ((intersections & 1) == 1);
    }

    public Vector3 GetCenter(bool atSurface)
    {
        var h = atSurface ? _heighest + Height : _lowest;
        return new Vector3(_boundingBox.Left + (_boundingBox.Width / 2f), _boundingBox.Top + (_boundingBox.Height / 2f), h);
    }
}
