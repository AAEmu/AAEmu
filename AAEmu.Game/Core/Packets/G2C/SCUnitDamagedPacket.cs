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
        private readonly byte _crimeState;
        private readonly int _absorbed;
        public int _manaBurn;
        
        public byte HoldableId { get; set; }
        public SkillHitType HitType { get; set; }

        public SCUnitDamagedPacket(CastAction castAction, SkillCaster skillCaster, uint casterId, uint targetId, int damage, int absorbed)
            : base(SCOffsets.SCUnitDamagedPacket, 5)
        {
            _castAction = castAction;
            _skillCaster = skillCaster;
            _casterId = casterId;
            _targetId = targetId;
            _damage = damage;
            _absorbed = absorbed;
        }

        public SCUnitDamagedPacket(CastAction castAction, SkillCaster skillCaster, uint casterId, uint targetId, int damage)
            : base(SCOffsets.SCUnitDamagedPacket, 5)
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
            stream.Write(_crimeState);       // crimeState [ 3.5 = 1 ]
            stream.WritePisc(_damage, _absorbed, 0);
            stream.WritePisc(0, 0, _manaBurn);
            stream.Write(HoldableId);        // hol        [ 3.5 = 19, 0 ]
            stream.Write((ushort)(288 | (ushort)HitType)); // de         [ damage effect? 3.5 = 5161, 289, 293 ]
            stream.Write((byte)1);           // flag       [ 3.5 = 9, 13 ]
            stream.Write((byte)1);           // result -> to debug info [ 3.5 = 1, 4 ]
            // TODO debug info
            return stream;
        }
    }
}
