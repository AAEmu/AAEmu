using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUnitPointsPacket(uint id, int health, int mana, int highAbilityRsc) : GamePacket(SCOffsets.SCUnitPointsPacket, 5)
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Trace;

    private readonly int _preciseHealth = health * 100;
    private readonly int _preciseMana = mana * 100;
    private readonly int _highAbilityRsc = highAbilityRsc;

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(id);
        stream.Write(_preciseHealth);
        stream.Write(_preciseMana);
        stream.Write(_highAbilityRsc);
        return stream;
    }
}
