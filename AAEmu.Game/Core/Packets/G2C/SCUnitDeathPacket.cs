using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUnitDeathPacket : GamePacket
    {
        private readonly uint _objId;
        private readonly byte _killReason;
        private readonly Unit _killer;

        public SCUnitDeathPacket(uint objId, byte killReason, Unit killer = null) : base(SCOffsets.SCUnitDeathPacket, 1)
        {
            _objId = objId;
            _killReason = killReason;
            _killer = killer;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_objId);
            stream.Write(_killReason);
            // ---------------
            stream.Write(15000u); // resurrectionWaitingTime
            stream.Write(0); // lostExp
            stream.Write((byte) 0); // deathDurabilityLossRatio
            // ---------------
            stream.WriteBc(_killer?.ObjId ?? 0);
            if (_killer != null)
            {
                // ---------------
                stream.Write((byte) 0); // GameType
                // ---------------
                stream.Write((ushort) 0); // killStreak
                stream.Write((byte) 0); // param1
                stream.Write((byte) 0); // param2
                stream.Write((byte) 0); // param3
                stream.Write(_killer.Name);

            }

            return stream;
        }
    }
}
