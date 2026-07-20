using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUnitVisualOptionsPacket(uint id, CharacterVisualOptions visualOptions)
    : GamePacket(SCOffsets.SCUnitVisualOptionsPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(id);
        stream.Write(visualOptions);
        return stream;
    }
}
