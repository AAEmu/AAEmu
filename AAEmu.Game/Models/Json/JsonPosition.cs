using System.Numerics;
using Jitter2.LinearMath;

namespace AAEmu.Game.Models.Json;

public class JsonPosition
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Roll { get; set; }
    public float Pitch { get; set; }
    public float Yaw { get; set; }

    public JVector AsPositionVector()
    {
        return new JVector(X, Y, Z);
    }
}
