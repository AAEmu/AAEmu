using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills.Plots;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCPlotEventPacket : GamePacket
    {
        private readonly ushort _tl;
        private readonly uint _eventId;
        private readonly uint _skillId;
        private readonly PlotObject _caster;
        private readonly PlotObject _target;
        private readonly uint _objId;
        private readonly ushort _castingTime;
        private readonly byte _flag;
        private readonly ulong _itemId;
        private readonly byte _targetUnitCount;

        public SCPlotEventPacket(ushort tl, uint eventId, uint skillId, PlotObject caster, PlotObject target,
            uint objId, ushort castingTime, byte flag, ulong itemId = 0L, byte targetUnitCount = 1)
            : base(SCOffsets.SCPlotEventPacket, 1)
        {
            _tl = tl;
            _eventId = eventId;
            _skillId = skillId;
            _caster = caster;
            _target = target;
            _objId = objId;
            _castingTime = castingTime;
            _flag = flag;
            _itemId = itemId;
            _targetUnitCount = targetUnitCount;
        }

        public override PacketStream Write(PacketStream stream)
        {
            _log.Warn("SCPlotEventPacket: tl = {0}, eventId {1}, skillId {2}, type {3}, targetId {4}, item {5}, objId {6}, castingTime {7}, targetUnitCount {8}, flag {9}",
                _tl, _eventId, _skillId, _caster.Type, _target.UnitId, _itemId, _objId, _castingTime, _targetUnitCount, _flag);

            stream.Write(_tl);      // tl
            stream.Write(_eventId); // eventId
            stream.Write(_skillId); // skillId
            stream.Write(_caster);  // PlotObj
                                    // type(b) Unit | Position
                                    // casterId(bc) | XYZ
            stream.Write(_target);  // PlotObj
                                    // type(b) Unit | Position
                                    // targetId(bc) | XYZ
            stream.Write(_itemId);  // itemObjId
            stream.WriteBc(_objId); // обычно 0, но иногда нужно вставлять casterId(bc)
            stream.Write(_castingTime); // msec, castingTime / 10
            stream.WriteBc(0);      // objId
            stream.Write((short)0); // msec
            stream.Write(_targetUnitCount); // targetUnitCount // TODO if aoe, list of units
            if (_targetUnitCount > 0)
            {
                for (var i = 0; i < _targetUnitCount; i++)
                {
                    stream.WriteBc(_target.UnitId); // targetId TODO targetUnitCount > 0 -> do->while() stream.WriteBc(0);
                }
            }
            stream.Write(_flag);
            if (((_flag >> 3) & 1) != 1)
            {
                return stream;           // flag = 2 | 6
            }
            for (var i = 0; i < 13; i++) // flag = 8
            {
                stream.Write(0); // v
            }
            return stream;
        }
    }
}
