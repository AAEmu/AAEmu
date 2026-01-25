using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Internal;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Packets.G2L;

/// <summary>
/// A packet sent by the game server to inform the login server about its current load.
/// </summary>
public class GLGameServerLoadPacket() : InternalPacket(TypeId), IInternalPacket
{
    public new static ushort TypeId => GLOffsets.GLGameServerLoadPacket;

    /// <summary>
    /// Gets the current load of the game server.
    /// </summary>
    public GSLoad Load { get; private set; }

    public override void Read(PacketStream stream)
    {
        Load = (GSLoad)stream.ReadByte();
    }
}
