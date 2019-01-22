using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSCreateSkillControllerPacket : GamePacket
    {
        public CSCreateSkillControllerPacket() : base(0x08a, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var objId = stream.ReadBc();
            var scType = stream.ReadByte();
            var fallDamageImmune = stream.ReadBoolean();

            _log.Debug("CreateSkillController, Type: {0}", scType);
        }
    }
}