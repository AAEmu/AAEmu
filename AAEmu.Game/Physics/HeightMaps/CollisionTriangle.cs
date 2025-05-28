using Jitter2.Collision;
using Jitter2.LinearMath;

namespace AAEmu.Game.Physics.HeightMaps;

public struct CollisionTriangle : ISupportMappable
{
    // ReSharper disable InconsistentNaming
    public JVector A, B, C;
    // ReSharper enable InconsistentNaming

    public void SupportMap(in JVector direction, out JVector result)
    {
        var min = JVector.Dot(A, direction);
        var dot = JVector.Dot(B, direction);

        result = A;
        if (dot > min)
        {
            min = dot;
            result = B;
        }

        dot = JVector.Dot(C, direction);
        if (dot > min)
        {
            result = C;
        }
    }

    public void GetCenter(out JVector point)
    {
        point = (1.0f / 3.0f) * (A + B + C);
    }
}
