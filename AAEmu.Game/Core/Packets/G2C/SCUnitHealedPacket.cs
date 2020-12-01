using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUnitHealedPacket : GamePacket
    {
        private readonly CastAction _castAction;
        private readonly SkillCaster _skillCaster;
        private readonly uint _targetId;
        private readonly byte _healType;
        private readonly byte _healHitType;
        private readonly int _value;
        
        public SCUnitHealedPacket(CastAction castAction, SkillCaster skillCaster, uint targetId, byte healType, byte healHitType, int value) 
            : base(SCOffsets.SCUnitHealedPacket, 1)
        {
            _castAction = castAction;
            _skillCaster = skillCaster;
            _targetId = targetId;
            _healType = healType; // 0 = Health, 1 = Mana
            _healHitType = healHitType; //11 = CriticalHealHit, 13 = HealHit, enum missing?
            _value = value;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_castAction);
            stream.Write(_skillCaster);
            stream.WriteBc(_targetId);
            stream.Write(_healType); // h
            stream.Write(_healHitType); // h
            stream.Write(_value); // a
            stream.Write(0); // o
            stream.Write((byte)1); // result -> to debug into
            return stream;
        }
    }
}
