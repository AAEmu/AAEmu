using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCPlotEventPacket : GamePacket
    {
        private readonly ushort _tl;
        private readonly uint _eventId;
        private readonly uint _skillId;
        private readonly uint _casterId;
        private readonly uint _targetId;
        private readonly uint _unkId;
        private readonly ushort _castingTime;
        private readonly byte _flag;

        public SCPlotEventPacket(ushort tl, uint eventId, uint skillId, uint casterId, uint targetId, uint unkId, ushort castingTime,
            byte flag) : base(SCOffsets.SCPlotEventPacket, 1)
        {
            _tl = tl;
            _eventId = eventId;
            _skillId = skillId;
            _casterId = casterId;
            _targetId = targetId;
            _unkId = unkId;
            _castingTime = castingTime;
            _flag = flag;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_tl);
            stream.Write(_eventId); // type(id), eventId?
            stream.Write(_skillId);
            // --------------------------------------------
            stream.Write((byte) 1); // type
            switch (1)
            {
                case 1:
                    stream.WriteBc(_casterId);
                    break;
                case 2:
                    stream.WritePosition(0f, 0f, 0f);
                    stream.Write((sbyte) 0); // rot.x
                    stream.Write((sbyte) 0); // rot.y
                    stream.Write((sbyte) 0); // rot.z
                    break;
            }

            // --------------------------------------------
            stream.Write((byte) 1); // type
            switch (1)
            {
                case 1:
                    stream.WriteBc(_targetId);
                    break;
                case 2:
                    stream.WritePosition(0f, 0f, 0f);
                    stream.Write((sbyte) 0); // rot.x
                    stream.Write((sbyte) 0); // rot.y
                    stream.Write((sbyte) 0); // rot.z
                    break;
            }

            // --------------------------------------------
            stream.Write(0L); // itemId
            stream.WriteBc(_unkId);
            stream.Write(_castingTime); // msec, castingTime / 10
            stream.WriteBc(0);
            stream.Write((short) 0); // msec

            stream.Write((byte) 1); // targetUnitCount // TODO if aoe, list of units
            // TODO targetUnitCount > 0 -> do->while() stream.WriteBc(0);
            stream.WriteBc(_targetId);

            stream.Write(_flag);

            if (((_flag >> 3) & 1) == 1)
            {
                for (var i = 0; i < 13; i++)
                    stream.Write(0); // v
            }

            return stream;
        }
    }
}
