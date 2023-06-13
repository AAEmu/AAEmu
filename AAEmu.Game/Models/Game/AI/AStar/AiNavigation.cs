/*
 * https://habr.com/ru/articles/513158/
 */

using System;
using System.Collections.Generic;

namespace AAEmu.Game.Models.Game.AI.AStar
{
    public class AiNavigation
    {
        public AiNavigation()
        {
        }

        public AiNavigation(uint id, uint zoneKey, uint startPoint, uint endPoint, float x, float y, float z)
        {
            Position = new Point();
            Position.X = x;
            Position.Y = y;
            Position.Z = z;
            Id = id;
            ZoneKey = zoneKey;
            StartPoint = startPoint;
            EndPoint = endPoint;
        }

        public AiNavigation(float x, float y, float z)
        {
            Position = new Point();
            Position.X = x;
            Position.Y = y;
            Position.Z = z;
            Id = 0;
            ZoneKey = 0;
            StartPoint = 0;
            EndPoint = 0;
        }

        public uint Id { get; set; }
        public uint ZoneKey { get; set; }
        public uint StartPoint { get; set; }
        public uint EndPoint { get; set; }
        public Point Position { get; set; }
    }

    public class Point : IEquatable<Point>, IComparable<Point>, IComparer<Point>
    {
        private static readonly double Sqr = Math.Sqrt(2);
        private readonly int hash;
        public static readonly Point Zero = new(0, 0, 0);

        public Point()
        {
        }

        public Point(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
            hash = HashCode.Combine(X, Y, Z);
        }

        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }

        /// <summary>
        /// Estimated path distance without obstacles.
        /// </summary>
        public double DistanceEstimate()
        {
            var linearSteps = Math.Abs(Math.Abs(Y) - Math.Abs(X));
            var diagonalSteps = Math.Max(Math.Abs(Y), Math.Abs(X)) - linearSteps;
            return linearSteps + Sqr * diagonalSteps;
        }

        public static Point operator +(Point a, Point b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Point operator -(Point a, Point b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public bool Equals(Point other) => other != null && X.Equals(other.X) && Y.Equals(other.Y);

        public override int GetHashCode() => hash;

        public override string ToString() => $"({X}, {Y}, {Z})";

        public int CompareTo(Point other)
        {
            if (ReferenceEquals(this, other))
            {
                return 0;
            }

            if (other is null)
            {
                return 1;
            }

            var xComparison = X.CompareTo(other.X);
            if (xComparison != 0)
            {
                return xComparison;
            }

            var yComparison = Y.CompareTo(other.Y);
            return yComparison;

        }

        public int Compare(Point x, Point y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (y is null)
            {
                return 1;
            }

            if (x is null)
            {
                return -1;
            }

            var xComparison = x.X.CompareTo(y.X);
            if (xComparison != 0)
            {
                return xComparison;
            }

            var yComparison = x.Y.CompareTo(y.Y);
            return yComparison;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            if (obj is null)
            {
                return false;
            }

            throw new NotImplementedException();
        }

        public static bool operator ==(Point left, Point right)
        {
            if (left is null)
            {
                return right is null;
            }

            return left.Equals(right);
        }

        public static bool operator !=(Point left, Point right)
        {
            return !(left == right);
        }

        public static bool operator <(Point left, Point right)
        {
            return left is null ? right is not null : left.CompareTo(right) < 0;
        }

        public static bool operator <=(Point left, Point right)
        {
            return left is null || left.CompareTo(right) <= 0;
        }

        public static bool operator >(Point left, Point right)
        {
            return left is not null && left.CompareTo(right) > 0;
        }

        public static bool operator >=(Point left, Point right)
        {
            return left is null ? right is null : left.CompareTo(right) >= 0;
        }
    }

    public class AiNavigationComparer : IComparer<AiNavigation>
    {
        public int Compare(AiNavigation p1, AiNavigation p2)
        {
            if(p1 is null || p2 is null)
            {
                throw new ArgumentException("Incorrect parameter value");
            }

            return p1.Position.CompareTo(p2.Position);
        }
    }
}
