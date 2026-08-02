using AAEmu.Commons.Network;

namespace AAEmu.World.Core.Packets.Wz;

/// <summary>
/// WZActivateNpcSpawnersInArea (0x042) — zone turns on npc_spawners.g instances in a circle.
/// Wire (17 B): f32 x, f32 y, f32 z, f32 radius, u8 activate (1=on).
/// </summary>
public class WZActivateNpcSpawnersInAreaPacket : ZonePacket
{
    public float X { get; }
    public float Y { get; }
    public float Z { get; }
    public float Radius { get; }
    public bool Activate { get; }

    public WZActivateNpcSpawnersInAreaPacket(float x, float y, float z, float radius, bool activate = true)
        : base(WzOpcodes.ActivateNpcSpawnersInArea)
    {
        X = x;
        Y = y;
        Z = z;
        Radius = radius;
        Activate = activate;
    }

    protected override void WriteBody(PacketStream stream)
    {
        stream.Write(X);
        stream.Write(Y);
        stream.Write(Z);
        stream.Write(Radius);
        stream.Write(Activate);
    }
}
