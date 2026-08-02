using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.Proxy;

/// <summary>Changes the CryEngine level used for the current connection state.</summary>
/// <remarks>Proxy opcode 0x00F carries string level, u64 checksum, and u8 immersive in that order.</remarks>
public class SetGameTypePacket(string level, ulong checksum, bool immersive)
    : GamePacket(PPOffsets.SetGameTypePacket, 2)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(level);
        stream.Write(checksum);
        stream.Write((byte)(immersive ? 1 : 0));
        return stream;
    }
}
