using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Echoes the difficulty the client picked in the H-window.
/// </summary>
/// <remarks>Wire: u8 difficult, u8 showUi.</remarks>
public class SCSelectedInstanceDifficultPacket(sbyte difficult, bool showUi)
    : GamePacket(SCOffsets.SCSelectedInstanceDifficultPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)difficult);
        stream.Write((byte)(showUi ? 1 : 0));
        return stream;
    }
}
