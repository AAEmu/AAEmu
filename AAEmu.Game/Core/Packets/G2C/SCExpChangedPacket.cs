using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// experience first and then applies the same delta to the active abilities when the flag is set.
/// </summary>
public class SCExpChangedPacket(uint objId, int exp, bool shouldAddAbilityExp)
    : GamePacket(SCOffsets.SCExpChangedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(objId);
        stream.Write(exp);
        stream.Write(shouldAddAbilityExp);
        return stream;
    }
}
