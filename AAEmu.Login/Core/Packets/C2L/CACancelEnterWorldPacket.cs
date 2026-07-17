using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.C2L;

/// <summary>
/// A packet sent by the client to cancel the process of entering a world.
/// </summary>
public class CACancelEnterWorldPacket() : LoginPacket(TypeId), ILoginPacket
{
    public new static ushort TypeId => CLOffsets.CACancelEnterWorldPacket;

    /// <summary>
    /// Gets the ID of the world the client is cancelling entry to.
    /// </summary>
    public byte WorldId { get; private set; }
    
    public override void Read(PacketStream stream)
    {
        WorldId = stream.ReadByte(); // wid -> world id
    }
}
