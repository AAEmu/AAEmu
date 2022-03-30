using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Static;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUnitDeathPacket : GamePacket
    {
        private readonly uint _objId;
        private readonly byte _killReason;
        private readonly Unit _killer;

        public SCUnitDeathPacket(uint objId, KillReason killReason, Unit killer = null) : base(SCOffsets.SCUnitDeathPacket, 5)
        {
            _objId = objId;
            _killReason = (byte)killReason;
            _killer = killer;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_objId);     // uid
            stream.Write(_killReason);  // killReason
            // ---------------
            stream.Write(0u);           // resurrectionWaitingTime
            stream.Write(0u);           // autoResurrectionWaitingTime added in 5.7.5.0
            stream.Write(0);            // lostExp
            stream.Write((byte)0);     // deathDurabilityLossRatio
            // ---------------
            stream.WriteBc(_killer?.ObjId ?? 0); // killer
            if (_killer == null)
                return stream;
            // ---------------
            stream.Write((byte)0);     // GameType
            // ---------------
            stream.Write((ushort)0);   // killStreak
            stream.Write((byte)0);     // param1
            stream.Write((byte)0);     // param2
            stream.Write((byte)0);     // param3
            stream.Write(_killer.Name); // killerName

            return stream;
        }
    }
}
