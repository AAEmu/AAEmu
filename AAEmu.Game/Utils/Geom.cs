using System;
using System.Collections.Generic;
using System.Numerics;

/*
  Some parts used from League.Sharp framework
*/
namespace AAEmu.Game.Utils
{
    public static class Geom
    {
        /// <summary>
        /// Converts a Vector3 to Vector2
        /// </summary>
        /// <param name="v">The v.</param>
        /// <returns></returns>
        public static Vector2 To2D(this Vector3 v)
        {
            return new Vector2(v.X, v.Y);
        }

        /// <summary>
        /// Returns the 2D distance (XY plane) between two vector.
        /// </summary>
        /// <param name="v">The v.</param>
        /// <param name="other">The other.</param>
        /// <param name="squared">if set to <c>true</c> [squared].</param>
        /// <returns></returns>
        public static float Distance(this Vector3 v, Vector3 other, bool squared = false)
        {
            return v.To2D().Distance(other, squared);
        }

        /// <summary>
        /// Returns the perpendicular vector.
        /// </summary>
        /// <param name="v">The v.</param>
        /// <returns></returns>
        public static Vector2 Perpendicular(this Vector2 v)
        {
            return new Vector2(-v.Y, v.X);
        }

        /// <summary>
        /// Returns the second perpendicular vector.
        /// </summary>
        /// <param name="v">The vector.</param>
        /// <returns></returns>
        public static Vector2 Perpendicular2(this Vector2 v)
        {
            return new Vector2(v.Y, -v.X);
        }

        /// <summary>
        /// Returns true if the vector is valid.
        /// </summary>
        /// <param name="v">The vector.</param>
        /// <returns></returns>
        public static bool IsValid(this Vector2 v)
        {
            return v != Vector2.Zero;
        }

        /// <summary>
        /// Determines whether this instance is valid.
        /// </summary>
        /// <param name="v">The vector.</param>
        /// <returns></returns>
        public static bool IsValid(this Vector3 v)
        {
            return v != Vector3.Zero;
        }

        /// <summary>
        /// Calculates the distance to the Vector2.
        /// </summary>
        /// <param name="v">The v.</param>
        /// <param name="to">To.</param>
        /// <param name="squared">if set to <c>true</c> gets the distance squared.</param>
        /// <returns></returns>
        public static float Distance(this Vector2 v, Vector2 to, bool squared = false)
        {
            return squared ? Vector2.DistanceSquared(v, to) : Vector2.Distance(v, to);
        }

        /// <summary>
        /// Calculates the distance to the Vector3.
        /// </summary>
        /// <param name="v">The v.</param>
        /// <param name="to">To.</param>
        /// <param name="squared">if set to <c>true</c> gets the distance squared.</param>
        /// <returns></returns>
        public static float Distance(this Vector2 v, Vector3 to, bool squared = false)
        {
            return v.Distance(to.To2D(), squared);
        }

        /// <summary>
        /// Returns the distance to the line segment.
        /// </summary>
        /// <param name="point">The point.</param>
        /// <param name="segmentStart">The segment start.</param>
        /// <param name="segmentEnd">The segment end.</param>
        /// <param name="onlyIfOnSegment">if set to <c>true</c> [only if on segment].</param>
        /// <param name="squared">if set to <c>true</c> [squared].</param>
        /// <returns></returns>
        public static float Distance(this Vector2 point,
            Vector2 segmentStart,
            Vector2 segmentEnd,
            bool onlyIfOnSegment = false,
            bool squared = false)
        {
            var objects = point.ProjectOn(segmentStart, segmentEnd);

            if (objects.IsOnSegment || onlyIfOnSegment == false)
            {
                return squared
                    ? Vector2.DistanceSquared(objects.SegmentPoint, point)
                    : Vector2.Distance(objects.SegmentPoint, point);
            }
            return float.MaxValue;
        }

        /// <summary>
        /// Returns the vector normalized.
        /// </summary>
        /// <param name="v">The vector.</param>
        /// <returns></returns>
        public static Vector2 Normalized(this Vector2 v)
        {
            v = Vector2.Normalize(v);
            return v;
        }

        /// <summary>
        /// Normalizes the specified vector.
        /// </summary>
        /// <param name="v">The vector.</param>
        /// <returns></returns>
        public static Vector3 Normalized(this Vector3 v)
        {
            v = Vector3.Normalize(v);
            return v;
        }

        /// <summary>
        /// Extends the vector.
        /// </summary>
        /// <param name="v">The vector.</param>
        /// <param name="to">The vector to extend to</param>
        /// <param name="distance">The distance to extend.</param>
        /// <returns></returns>
        public static Vector2 Extend(this Vector2 v, Vector2 to, float distance)
        {
            return v + distance * (to - v).Normalized();
        }

        /// <summary>
        /// Extends the specified vector.
        /// </summary>
        /// <param name="v">The vector.</param>
        /// <param name="to">The vector to extend to.</param>
        /// <param name="distance">The distance.</param>
        /// <returns></returns>
        public static Vector3 Extend(this Vector3 v, Vector3 to, float distance)
        {
            return v + distance * (to - v).Normalized();
        }

        /// <summary>
        /// Shortens the specified vector.
        /// </summary>
        /// <param name="v">The vector.</param>
        /// <param name="to">The vector to shorten from.</param>
        /// <param name="distance">The distance.</param>
        /// <returns></returns>
        public static Vector2 Shorten(this Vector2 v, Vector2 to, float distance)
        {
            return v - distance * (to - v).Normalized();
        }

        /// <summary>
        /// Shortens the specified vector.
        /// </summary>
        /// <param name="v">The vector.</param>
        /// <param name="to">The vector to shorten from.</param>
        /// <param name="distance">The distance.</param>
        /// <returns></returns>
        public static Vector3 Shorten(this Vector3 v, Vector3 to, float distance)
        {
            return v - distance * (to - v).Normalized();
        }

        /// <summary>
        /// Rotates the vector a set angle (angle in radians).
        /// </summary>
        /// <param name="v">The vector.</param>
        /// <param name="angle">The angle.</param>
        /// <returns></returns>
        public static Vector2 Rotated(this Vector2 v, float angle)
        {
            var c = Math.Cos(angle);
            var s = Math.Sin(angle);

            return new Vector2((float)(v.X * c - v.Y * s), (float)(v.Y * c + v.X * s));
        }

        /// <summary>
        /// Returns the cross product Z value.
        /// </summary>
        /// <param name="self">The self.</param>
        /// <param name="other">The other.</param>
        /// <returns></returns>
        public static float CrossProduct(this Vector2 self, Vector2 other)
        {
            return other.Y * self.X - other.X * self.Y;
        }
        /// <summary>
        /// Returns the projection of the Vector2 on the segment.
        /// </summary>
        /// <param name="point">The point.</param>
        /// <param name="segmentStart">The segment start.</param>
        /// <param name="segmentEnd">The segment end.</param>
        /// <returns></returns>
        public static ProjectionInfo ProjectOn(this Vector2 point, Vector2 segmentStart, Vector2 segmentEnd)
        {
            var cx = point.X;
            var cy = point.Y;
            var ax = segmentStart.X;
            var ay = segmentStart.Y;
            var bx = segmentEnd.X;
            var by = segmentEnd.Y;
            var rL = ((cx - ax) * (bx - ax) + (cy - ay) * (by - ay)) /
                     ((float)Math.Pow(bx - ax, 2) + (float)Math.Pow(by - ay, 2));
            var pointLine = new Vector2(ax + rL * (bx - ax), ay + rL * (by - ay));
            float rS;
            if (rL < 0)
            {
                rS = 0;
            }
            else if (rL > 1)
            {
                rS = 1;
            }
            else
            {
                rS = rL;
            }

            var isOnSegment = rS.CompareTo(rL) == 0;
            var pointSegment = isOnSegment ? pointLine : new Vector2(ax + rS * (bx - ax), ay + rS * (@by - ay));
            return new ProjectionInfo(isOnSegment, pointSegment, pointLine);
        }
        /// <summary>
        /// Intersects two line segments.
        /// </summary>
        /// <param name="lineSegment1Start">The line segment1 start.</param>
        /// <param name="lineSegment1End">The line segment1 end.</param>
        /// <param name="lineSegment2Start">The line segment2 start.</param>
        /// <param name="lineSegment2End">The line segment2 end.</param>
        /// <returns></returns>
        public static IntersectionResult Intersection(this Vector2 lineSegment1Start, Vector2 lineSegment1End, Vector2 lineSegment2Start, Vector2 lineSegment2End)
        {
            double deltaACy = lineSegment1Start.Y - lineSegment2Start.Y;
            double deltaDCx = lineSegment2End.X - lineSegment2Start.X;
            double deltaACx = lineSegment1Start.X - lineSegment2Start.X;
            double deltaDCy = lineSegment2End.Y - lineSegment2Start.Y;
            double deltaBAx = lineSegment1End.X - lineSegment1Start.X;
            double deltaBAy = lineSegment1End.Y - lineSegment1Start.Y;

            var denominator = deltaBAx * deltaDCy - deltaBAy * deltaDCx;
            var numerator = deltaACy * deltaDCx - deltaACx * deltaDCy;

            if (Math.Abs(denominator) < float.Epsilon)
            {
                if (Math.Abs(numerator) < float.Epsilon)
                {
                    // collinear. Potentially infinite intersection points.
                    // Check and return one of them.
                    if (lineSegment1Start.X >= lineSegment2Start.X && lineSegment1Start.X <= lineSegment2End.X)
                    {
                        return new IntersectionResult(true, lineSegment1Start);
                    }
                    if (lineSegment2Start.X >= lineSegment1Start.X && lineSegment2Start.X <= lineSegment1End.X)
                    {
                        return new IntersectionResult(true, lineSegment2Start);
                    }
                    return new IntersectionResult();
                }
                // parallel
                return new IntersectionResult();
            }

            var r = numerator / denominator;
            if (r < 0 || r > 1)
            {
                return new IntersectionResult();
            }

            var s = (deltaACy * deltaBAx - deltaACx * deltaBAy) / denominator;
            if (s < 0 || s > 1)
            {
                return new IntersectionResult();
            }

            return new IntersectionResult(
                true,
                new Vector2((float)(lineSegment1Start.X + r * deltaBAx), (float)(lineSegment1Start.Y + r * deltaBAy)));
        }

        /// <summary>
        /// Gets the vectors movement collision.
        /// </summary>
        /// <param name="startPoint1">The start point1.</param>
        /// <param name="endPoint1">The end point1.</param>
        /// <param name="v1">The v1.</param>
        /// <param name="startPoint2">The start point2.</param>
        /// <param name="v2">The v2.</param>
        /// <param name="delay">The delay.</param>
        /// <returns></returns>
        public static object[] VectorMovementCollision(Vector2 startPoint1, Vector2 endPoint1, float v1, Vector2 startPoint2, float v2, float delay = 0f)
        {
            float sP1X = startPoint1.X, sP1Y = startPoint1.Y, eP1X = endPoint1.X, eP1Y = endPoint1.Y, sP2X = startPoint2.X, sP2Y = startPoint2.Y;
            float d = eP1X - sP1X, e = eP1Y - sP1Y;
            float dist = (float)Math.Sqrt(d * d + e * e), t1 = float.NaN;
            float s = Math.Abs(dist) > float.Epsilon ? v1 * d / dist : 0, k = (Math.Abs(dist) > float.Epsilon) ? v1 * e / dist : 0f;
            float r = sP2X - sP1X, j = sP2Y - sP1Y;
            var c = r * r + j * j;

            if (dist > 0f)
            {
                if (Math.Abs(v1 - float.MaxValue) < float.Epsilon)
                {
                    var t = dist / v1;
                    t1 = v2 * t >= 0f ? t : float.NaN;
                }
                else if (Math.Abs(v2 - float.MaxValue) < float.Epsilon)
                {
                    t1 = 0f;
                }
                else
                {
                    float a = s * s + k * k - v2 * v2, b = -r * s - j * k;

                    if (Math.Abs(a) < float.Epsilon)
                    {
                        if (Math.Abs(b) < float.Epsilon)
                        {
                            t1 = (Math.Abs(c) < float.Epsilon) ? 0f : float.NaN;
                        }
                        else
                        {
                            var t = -c / (2 * b);
                            t1 = (v2 * t >= 0f) ? t : float.NaN;
                        }
                    }
                    else
                    {
                        var sqr = b * b - a * c;
                        if (sqr >= 0)
                        {
                            var nom = (float)Math.Sqrt(sqr);
                            var t = (-nom - b) / a;
                            t1 = v2 * t >= 0f ? t : float.NaN;
                            t = (nom - b) / a;
                            var t2 = (v2 * t >= 0f) ? t : float.NaN;

                            if (!float.IsNaN(t2) && !float.IsNaN(t1))
                            {
                                if (t1 >= delay && t2 >= delay)
                                {
                                    t1 = Math.Min(t1, t2);
                                }
                                else if (t2 >= delay)
                                {
                                    t1 = t2;
                                }
                            }
                        }
                    }
                }
            }
            else if (Math.Abs(dist) < float.Epsilon)
            {
                t1 = 0f;
            }

            return new object[] { t1, (!float.IsNaN(t1)) ? new Vector2(sP1X + s * t1, sP1Y + k * t1) : new Vector2() };
        }
    }
    /// <summary>
    /// Represents the projection information.
    /// </summary>
    public struct ProjectionInfo
    {
        /// <summary>
        /// The is on segment
        /// </summary>
        public bool IsOnSegment;

        /// <summary>
        /// The line point
        /// </summary>
        public Vector2 LinePoint;

        /// <summary>
        /// The segment point
        /// </summary>
        public Vector2 SegmentPoint;

        /// <summary>
        /// Initializes a new instance of the <see cref="ProjectionInfo"/> struct.
        /// </summary>
        /// <param name="isOnSegment">if set to <c>true</c> [is on segment].</param>
        /// <param name="segmentPoint">The segment point.</param>
        /// <param name="linePoint">The line point.</param>
        public ProjectionInfo(bool isOnSegment, Vector2 segmentPoint, Vector2 linePoint)
        {
            IsOnSegment = isOnSegment;
            SegmentPoint = segmentPoint;
            LinePoint = linePoint;
        }
    }

    /// <summary>
    /// Represents an intersection result.
    /// </summary>
    public struct IntersectionResult
    {
        /// <summary>
        /// If they intersect.
        /// </summary>
        public bool Intersects;

        /// <summary>
        /// The point
        /// </summary>
        public Vector2 Point;

        /// <summary>
        /// Initializes a new instance of the <see cref="IntersectionResult"/> struct.
        /// </summary>
        /// <param name="intersects">if set to <c>true</c>, they insersect.</param>
        /// <param name="point">The point.</param>
        public IntersectionResult(bool intersects = false, Vector2 point = new Vector2())
        {
            Intersects = intersects;
            Point = point;
        }
    }
    /// <summary>
    /// Represents a polygon.
    /// </summary>
    public class Polygon
    {
        /// <summary>
        /// The points
        /// </summary>
        public List<Vector2> Points = new List<Vector2>();

        /// <summary>
        /// Adds the specified point.
        /// </summary>
        /// <param name="point">The point.</param>
        public void Add(Vector2 point)
        {
            Points.Add(point);
        }

        /// <summary>
        /// Adds the specified point.
        /// </summary>
        /// <param name="point">The point.</param>
        public void Add(Vector3 point)
        {
            Points.Add(point.To2D());
        }

        /// <summary>
        /// Adds the specified polygon.
        /// </summary>
        /// <param name="polygon">The polygon.</param>
        public void Add(Polygon polygon)
        {
            foreach (var point in polygon.Points)
            {
                Points.Add(point);
            }
        }

        /// <summary>
        /// Represnets an arc polygon.
        /// </summary>
        public class Arc : Polygon
        {
            /// <summary>
            /// The angle
            /// </summary>
            public float Angle;

            /// <summary>
            /// The end position
            /// </summary>
            public Vector2 EndPos;

            /// <summary>
            /// The radius
            /// </summary>
            public float Radius;

            /// <summary>
            /// The start position
            /// </summary>
            public Vector2 StartPos;

            /// <summary>
            /// The quality
            /// </summary>
            private readonly int _quality;

            /// <summary>
            /// Initializes a new instance of the <see cref="Polygon.Arc"/> class.
            /// </summary>
            /// <param name="start">The start.</param>
            /// <param name="direction">The direction.</param>
            /// <param name="angle">The angle.</param>
            /// <param name="radius">The radius.</param>
            /// <param name="quality">The quality.</param>
            public Arc(Vector3 start, Vector3 direction, float angle, float radius, int quality = 20)
                : this(start.To2D(), direction.To2D(), angle, radius, quality)
            { }

            /// <summary>
            /// Initializes a new instance of the <see cref="Polygon.Arc"/> class.
            /// </summary>
            /// <param name="start">The start.</param>
            /// <param name="direction">The direction.</param>
            /// <param name="angle">The angle.</param>
            /// <param name="radius">The radius.</param>
            /// <param name="quality">The quality.</param>
            public Arc(Vector2 start, Vector2 direction, float angle, float radius, int quality = 20)
            {
                StartPos = start;
                EndPos = (direction - start).Normalized();
                Angle = angle;
                Radius = radius;
                _quality = quality;
                UpdatePolygon();
            }

            /// <summary>
            /// Updates the polygon.
            /// </summary>
            /// <param name="offset">The offset.</param>
            public void UpdatePolygon(int offset = 0)
            {
                Points.Clear();
                var outRadius = (Radius + offset) / (float)Math.Cos(2 * Math.PI / _quality);
                var side1 = EndPos.Rotated(-Angle * 0.5f);
                for (var i = 0; i <= _quality; i++)
                {
                    var cDirection = side1.Rotated(i * Angle / _quality).Normalized();
                    Points.Add(
                        new Vector2(StartPos.X + outRadius * cDirection.X, StartPos.Y + outRadius * cDirection.Y));
                }
            }
        }

        /// <summary>
        /// Represents a line polygon.
        /// </summary>
        public class Line : Polygon
        {
            /// <summary>
            /// The line start
            /// </summary>
            public Vector2 LineStart;

            /// <summary>
            /// The line end
            /// </summary>
            public Vector2 LineEnd;

            /// <summary>
            /// Gets or sets the length.
            /// </summary>
            /// <value>
            /// The length.
            /// </value>
            public float Length
            {
                get { return LineStart.Distance(LineEnd); }
                set { LineEnd = (LineEnd - LineStart).Normalized() * value + LineStart; }
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="Polygon.Line"/> class.
            /// </summary>
            /// <param name="start">The start.</param>
            /// <param name="end">The end.</param>
            /// <param name="length">The length.</param>
            public Line(Vector3 start, Vector3 end, float length = -1) : this(start.To2D(), end.To2D(), length) { }

            /// <summary>
            /// Initializes a new instance of the <see cref="Polygon.Line"/> class.
            /// </summary>
            /// <param name="start">The start.</param>
            /// <param name="end">The end.</param>
            /// <param name="length">The length.</param>
            public Line(Vector2 start, Vector2 end, float length = -1)
            {
                LineStart = start;
                LineEnd = end;
                if (length > 0)
                {
                    Length = length;
                }
                UpdatePolygon();
            }

            /// <summary>
            /// Updates the polygon.
            /// </summary>
            public void UpdatePolygon()
            {
                Points.Clear();
                Points.Add(LineStart);
                Points.Add(LineEnd);
            }
        }

        /// <summary>
        /// Represents a circle polygon.
        /// </summary>
        public class Circle : Polygon
        {
            /// <summary>
            /// The center
            /// </summary>
            public Vector2 Center;

            /// <summary>
            /// The radius
            /// </summary>
            public float Radius;

            /// <summary>
            /// The quality
            /// </summary>
            private readonly int _quality;

            /// <summary>
            /// Initializes a new instance of the <see cref="Polygon.Circle"/> class.
            /// </summary>
            /// <param name="center">The center.</param>
            /// <param name="radius">The radius.</param>
            /// <param name="quality">The quality.</param>
            public Circle(Vector3 center, float radius, int quality = 20) : this(center.To2D(), radius, quality) { }

            /// <summary>
            /// Initializes a new instance of the <see cref="Polygon.Circle"/> class.
            /// </summary>
            /// <param name="center">The center.</param>
            /// <param name="radius">The radius.</param>
            /// <param name="quality">The quality.</param>
            public Circle(Vector2 center, float radius, int quality = 20)
            {
                Center = center;
                Radius = radius;
                _quality = quality;
                UpdatePolygon();
            }

            /// <summary>
            /// Updates the polygon.
            /// </summary>
            /// <param name="offset">The offset.</param>
            /// <param name="overrideWidth">Width of the override.</param>
            public void UpdatePolygon(int offset = 0, float overrideWidth = -1)
            {
                Points.Clear();
                var outRadius = (overrideWidth > 0
                    ? overrideWidth
                    : (offset + Radius) / (float)Math.Cos(2 * Math.PI / _quality));
                for (var i = 1; i <= _quality; i++)
                {
                    var angle = i * 2 * Math.PI / _quality;
                    var point = new Vector2(
                        Center.X + outRadius * (float)Math.Cos(angle), Center.Y + outRadius * (float)Math.Sin(angle));
                    Points.Add(point);
                }
            }
        }

        /// <summary>
        /// Represents a rectangle polygon.
        /// </summary>
        public class Rectangle : Polygon
        {
            /// <summary>
            /// Gets the direction.
            /// </summary>
            /// <value>
            /// The direction.
            /// </value>
            public Vector2 Direction => (End - Start).Normalized();

            /// <summary>
            /// Gets the perpendicular.
            /// </summary>
            /// <value>
            /// The perpendicular.
            /// </value>
            public Vector2 Perpendicular => Direction.Perpendicular();

            /// <summary>
            /// The end
            /// </summary>
            public Vector2 End;

            /// <summary>
            /// The start
            /// </summary>
            public Vector2 Start;

            /// <summary>
            /// The width
            /// </summary>
            public float Width;

            /// <summary>
            /// Initializes a new instance of the <see cref="Rectangle"/> class.
            /// </summary>
            /// <param name="start">The start.</param>
            /// <param name="end">The end.</param>
            /// <param name="width">The width.</param>
            public Rectangle(Vector3 start, Vector3 end, float width) : this(start.To2D(), end.To2D(), width) { }

            /// <summary>
            /// Initializes a new instance of the <see cref="Rectangle"/> class.
            /// </summary>
            /// <param name="start">The start.</param>
            /// <param name="end">The end.</param>
            /// <param name="width">The width.</param>
            public Rectangle(Vector2 start, Vector2 end, float width)
            {
                Start = start;
                End = end;
                Width = width;
                UpdatePolygon();
            }

            /// <summary>
            /// Updates the polygon.
            /// </summary>
            /// <param name="offset">The offset.</param>
            /// <param name="overrideWidth">Width of the override.</param>
            public void UpdatePolygon(int offset = 0, float overrideWidth = -1)
            {
                Points.Clear();
                Points.Add(
                    Start + (overrideWidth > 0 ? overrideWidth : Width + offset) * Perpendicular - offset * Direction);
                Points.Add(
                    Start - (overrideWidth > 0 ? overrideWidth : Width + offset) * Perpendicular - offset * Direction);
                Points.Add(
                    End - (overrideWidth > 0 ? overrideWidth : Width + offset) * Perpendicular + offset * Direction);
                Points.Add(
                    End + (overrideWidth > 0 ? overrideWidth : Width + offset) * Perpendicular + offset * Direction);
            }
        }

        /// <summary>
        /// Represents a ring polygon.
        /// </summary>
        public class Ring : Polygon
        {
            /// <summary>
            /// The center
            /// </summary>
            public Vector2 Center;

            /// <summary>
            /// The inner radius
            /// </summary>
            public float InnerRadius;

            /// <summary>
            /// The outer radius
            /// </summary>
            public float OuterRadius;

            /// <summary>
            /// The quality
            /// </summary>
            private readonly int _quality;

            /// <summary>
            /// Initializes a new instance of the <see cref="Polygon.Ring"/> class.
            /// </summary>
            /// <param name="center">The center.</param>
            /// <param name="innerRadius">The inner radius.</param>
            /// <param name="outerRadius">The outer radius.</param>
            /// <param name="quality">The quality.</param>
            public Ring(Vector3 center, float innerRadius, float outerRadius, int quality = 20)
                : this(center.To2D(), innerRadius, outerRadius, quality)
            { }

            /// <summary>
            /// Initializes a new instance of the <see cref="Polygon.Ring"/> class.
            /// </summary>
            /// <param name="center">The center.</param>
            /// <param name="innerRadius">The inner radius.</param>
            /// <param name="outerRadius">The outer radius.</param>
            /// <param name="quality">The quality.</param>
            public Ring(Vector2 center, float innerRadius, float outerRadius, int quality = 20)
            {
                Center = center;
                InnerRadius = innerRadius;
                OuterRadius = outerRadius;
                _quality = quality;
                UpdatePolygon();
            }

            /// <summary>
            /// Updates the polygon.
            /// </summary>
            /// <param name="offset">The offset.</param>
            public void UpdatePolygon(int offset = 0)
            {
                Points.Clear();
                var outRadius = (offset + InnerRadius + OuterRadius) / (float)Math.Cos(2 * Math.PI / _quality);
                var innerRadius = InnerRadius - OuterRadius - offset;
                for (var i = 0; i <= _quality; i++)
                {
                    var angle = i * 2 * Math.PI / _quality;
                    var point = new Vector2(
                        Center.X - outRadius * (float)Math.Cos(angle), Center.Y - outRadius * (float)Math.Sin(angle));
                    Points.Add(point);
                }
                for (var i = 0; i <= _quality; i++)
                {
                    var angle = i * 2 * Math.PI / _quality;
                    var point = new Vector2(
                        Center.X + innerRadius * (float)Math.Cos(angle),
                        Center.Y - innerRadius * (float)Math.Sin(angle));
                    Points.Add(point);
                }
            }
        }

        /// <summary>
        /// Represnets a sector polygon.
        /// </summary>
        public class Sector : Polygon
        {
            /// <summary>
            /// The angle
            /// </summary>
            public float Angle;

            /// <summary>
            /// The center
            /// </summary>
            public Vector2 Center;

            /// <summary>
            /// The direction
            /// </summary>
            public Vector2 Direction;

            /// <summary>
            /// The radius
            /// </summary>
            public float Radius;

            /// <summary>
            /// The quality
            /// </summary>
            private readonly int _quality;

            /// <summary>
            /// Initializes a new instance of the <see cref="Polygon.Sector"/> class.
            /// </summary>
            /// <param name="center">The center.</param>
            /// <param name="direction">The direction.</param>
            /// <param name="angle">The angle.</param>
            /// <param name="radius">The radius.</param>
            /// <param name="quality">The quality.</param>
            public Sector(Vector3 center, Vector3 direction, float angle, float radius, int quality = 20)
                : this(center.To2D(), direction.To2D(), angle, radius, quality)
            { }

            /// <summary>
            /// Initializes a new instance of the <see cref="Polygon.Sector"/> class.
            /// </summary>
            /// <param name="center">The center.</param>
            /// <param name="direction">The direction.</param>
            /// <param name="angle">The angle.</param>
            /// <param name="radius">The radius.</param>
            /// <param name="quality">The quality.</param>
            public Sector(Vector2 center, Vector2 direction, float angle, float radius, int quality = 20)
            {
                Center = center;
                Direction = (direction - center).Normalized();
                Angle = angle;
                Radius = radius;
                _quality = quality;
                UpdatePolygon();
            }

            /// <summary>
            /// Updates the polygon.
            /// </summary>
            /// <param name="offset">The offset.</param>
            public void UpdatePolygon(int offset = 0)
            {
                Points.Clear();
                var outRadius = (Radius + offset) / (float)Math.Cos(2 * Math.PI / _quality);
                Points.Add(Center);
                var side1 = Direction.Rotated(-Angle * 0.5f);
                for (var i = 0; i <= _quality; i++)
                {
                    var cDirection = side1.Rotated(i * Angle / _quality).Normalized();
                    Points.Add(new Vector2(Center.X + outRadius * cDirection.X, Center.Y + outRadius * cDirection.Y));
                }
            }


            /// <summary>
            /// Rotates Line by angle/radian
            /// </summary>
            /// <param name="point1"></param>
            /// <param name="point2"></param>
            /// <param name="value"></param>
            /// <param name="radian">True for radian values, false for degree</param>
            /// <returns></returns>
            public Vector2 RotateLineFromPoint(Vector2 point1, Vector2 point2, float value, bool radian = true)
            {
                var angle = !radian ? value * Math.PI / 180 : value;
                var line = Vector2.Subtract(point2, point1);

                var newline = new Vector2
                {
                    X = (float)(line.X * Math.Cos(angle) - line.Y * Math.Sin(angle)),
                    Y = (float)(line.X * Math.Sin(angle) + line.Y * Math.Cos(angle))
                };

                return Vector2.Add(newline, point1);
            }
        }
    }
}
