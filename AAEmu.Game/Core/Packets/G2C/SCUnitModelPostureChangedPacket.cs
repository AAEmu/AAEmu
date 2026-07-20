using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

using Unit = AAEmu.Game.Models.Game.Units.Unit;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUnitModelPostureChangedPacket(Unit unit, uint animActionId, bool activateAnimation)
    : GamePacket(SCOffsets.SCUnitModelPostureChangedPacket, 5)
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Off;

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(unit.ObjId);
        Unit.ModelPosture(stream, unit, animActionId, activateAnimation);
        return stream;
    }
}
