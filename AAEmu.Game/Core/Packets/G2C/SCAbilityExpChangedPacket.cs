using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAbilityExpChangedPacket(uint objId, AbilityType ability, int exp)
    : GamePacket(SCOffsets.SCAbilityExpChangedPacket, 5)
{
    private readonly byte _ability = (byte)ability;

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(objId);
        stream.Write(_ability);
        stream.Write(exp);
        return stream;
    }
}
