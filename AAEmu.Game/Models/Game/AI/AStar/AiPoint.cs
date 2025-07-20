namespace AAEmu.Game.Models.Game.AI.AStar;

public class AiPoint : IEquatable<AiPoint>
{
    private static readonly double s_sqr2 = Math.Sqrt(2);
    private readonly int _hash;
    // ReSharper disable once InconsistentNaming
    public static readonly AiPoint Zero = new(0, 0, 0);

    public AiPoint()
    {
        //
    }

    public AiPoint(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
        _hash = HashCode.Combine(X, Y, Z);
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
        return linearSteps + s_sqr2 * diagonalSteps;
    }

    public static AiPoint operator +(AiPoint a, AiPoint b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static AiPoint operator -(AiPoint a, AiPoint b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public bool Equals(AiPoint other) => other != null && X.Equals(other.X) && Y.Equals(other.Y);

    public override int GetHashCode() => _hash;

    public override string ToString() => $"({X}, {Y}, {Z})";

    public override bool Equals(object obj)
    {
        return Equals(obj as AiPoint);
    }
}
