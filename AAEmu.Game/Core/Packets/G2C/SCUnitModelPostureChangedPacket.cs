using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;

using Unit = AAEmu.Game.Models.Game.Units.Unit;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUnitModelPostureChangedPacket : GamePacket
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Off;
    
    private readonly Unit _unit;
    private readonly uint _animActionId;
    private readonly bool _activateAnimation;

    public SCUnitModelPostureChangedPacket(Unit unit, uint animActionId, bool activateAnimation) : base(SCOffsets.SCUnitModelPostureChangedPacket, 5)
    {
        _unit = unit;
        _animActionId = animActionId;
        _activateAnimation = activateAnimation;
    }
    
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(_unit.ObjId);
        Unit.ModelPosture(stream, _unit, _animActionId, _activateAnimation);
        return stream;
    }
}
