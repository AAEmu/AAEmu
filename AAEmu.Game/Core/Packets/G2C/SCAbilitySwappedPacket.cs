using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCAbilitySwappedPacket : GamePacket
    {
        private readonly uint _objId;
        private readonly AbilityType _oldAbilityId;
        private readonly AbilityType _abilityId;

        public SCAbilitySwappedPacket(uint objId, AbilityType oldAbilityId, AbilityType abilityId) : base(SCOffsets.SCAbilitySwappedPacket, 5)
        {
            _objId = objId;
            _oldAbilityId = oldAbilityId;
            _abilityId = abilityId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_objId);
            stream.Write((byte) _oldAbilityId);
            stream.Write((byte) _abilityId);
            return stream;
        }
    }
}
