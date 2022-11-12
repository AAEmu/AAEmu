using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Units;

using Unit = AAEmu.Game.Models.Game.Units.Unit;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUnitModelPostureChangedPacket : GamePacket
    {
        private Unit _unit;
        private BaseUnitType _baseUnitType;
        private ModelPostureType _modelPostureType;
        private uint _animActionId;

        public SCUnitModelPostureChangedPacket(Unit unit, BaseUnitType baseUnitType, ModelPostureType modelPostureType, uint animActionId = 0xFFFFFFFF) : base(SCOffsets.SCUnitModelPostureChangedPacket, 1)
        {
            _unit = unit;
            _baseUnitType = baseUnitType;
            _modelPostureType = modelPostureType;
            _animActionId = animActionId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_unit.ObjId);
            _unit.ModelPosture(stream, _unit, _baseUnitType, _modelPostureType, _animActionId);

            return stream;
        }
    }
}
