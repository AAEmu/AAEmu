using System;

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
    
    public class Point : IEquatable<Point>
    {
        private static readonly double Sqr = Math.Sqrt(2);
        private readonly int hash;

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

        public bool Equals(Point other)
            => other != null && X.Equals(other.X) && Y.Equals(other.Y);

        public override int GetHashCode()
            => hash;

        public override string ToString()
            => $"({X}, {Y}, {Z})";
    }
}
