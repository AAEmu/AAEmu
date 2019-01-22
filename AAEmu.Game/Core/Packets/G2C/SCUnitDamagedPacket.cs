using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUnitDamagedPacket : GamePacket
    {
        private readonly CastAction _castAction;
        private readonly SkillAction _skillAction;
        private readonly uint _casterId;
        private readonly uint _targetId;
        private readonly int _damage;

        public SCUnitDamagedPacket(CastAction castAction, SkillAction skillAction, uint casterId, uint targetId, int damage) : base(0x0a0, 1)
        {
            _castAction = castAction;
            _skillAction = skillAction;
            _casterId = casterId;
            _targetId = targetId;
            _damage = damage;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_castAction);
            stream.Write(_skillAction);
            stream.WriteBc(_casterId);
            stream.WriteBc(_targetId);
            stream.Write((byte) 0); // crimeState
            stream.WritePisc(_damage, 0, 0);
            stream.WritePisc(0, 0, 0);
            stream.Write((byte) 0); // hol
            stream.Write((short) 289); // de
            stream.Write((byte) 1); // flag
            // TODO debug info
            return stream;
        }
    }
}