using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.C2L;

/// <summary>
/// A packet sent by the client in response to an <see cref="AAEmu.Login.Core.Packets.L2C.ACPingPacket"/> heartbeat.
/// </summary>
public class CAPongPacket() : LoginPacket(TypeId), ILoginPacket
{
    public new static ushort TypeId => CLOffsets.CAPongPacket;

    /// <summary>
    /// Gets the timestamp echoed back from the server's ping.
    /// </summary>
    public ulong Send { get; private set; }

    public override void Read(PacketStream stream)
    {
        Send = stream.ReadUInt64();
    }
}