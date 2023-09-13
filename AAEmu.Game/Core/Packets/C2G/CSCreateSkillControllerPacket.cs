using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSCreateSkillControllerPacket : GamePacket
    {
        public CSCreateSkillControllerPacket() : base(CSOffsets.CSCreateSkillControllerPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var objId = stream.ReadBc();
            var scType = stream.ReadByte();
            var fallDamageImmune = stream.ReadBoolean();

            _log.Debug("CreateSkillController, objId: {0}, scType: {1}, fallDamageImmune: {2}", objId, scType, fallDamageImmune);
        }
    }
}
