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
        private readonly byte _type;
        private readonly int _value;
        
        public SCUnitHealedPacket(CastAction castAction, SkillCaster skillCaster, uint targetId, byte type, int value) 
            : base(SCOffsets.SCUnitHealedPacket, 1)
        {
            _castAction = castAction;
            _skillCaster = skillCaster;
            _targetId = targetId;
            _type = type;
            _value = value;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_castAction);
            stream.Write(_skillCaster);
            stream.WriteBc(_targetId);
            stream.Write(_type); // h
            stream.Write((byte)13); // h
            stream.Write(_value); // a
            stream.Write(0); // o
            stream.Write((byte)1); // result -> to debug into
            return stream;
        }
    }
}
