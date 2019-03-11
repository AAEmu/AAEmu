using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUnitDamagedPacket : GamePacket
    {
        private readonly CastAction _castAction;
        private readonly SkillCaster _skillCaster;
        private readonly uint _casterId;
        private readonly uint _targetId;
        private readonly int _damage;

        public SCUnitDamagedPacket(CastAction castAction, SkillCaster skillCaster, uint casterId, uint targetId, int damage)
            : base(SCOffsets.SCUnitDamagedPacket, 1)
        {
            _castAction = castAction;
            _skillCaster = skillCaster;
            _casterId = casterId;
            _targetId = targetId;
            _damage = damage;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_castAction);
            stream.Write(_skillCaster);
            stream.WriteBc(_casterId);
            stream.WriteBc(_targetId);
            stream.Write((byte)0); // crimeState
            stream.WritePisc(_damage, 0, 0);
            stream.WritePisc(0, 0, 0);
            stream.Write((byte)0); // hol
            stream.Write((ushort)289); // de
            stream.Write((byte)1); // flag
            stream.Write((byte)1); // result -> to debug info
            // TODO debug info
            return stream;
        }
    }
}
