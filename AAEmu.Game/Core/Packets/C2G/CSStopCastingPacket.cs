using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSStopCastingPacket : GamePacket
    {
        public CSStopCastingPacket() : base(0x054, 1)
        {
        }

        public override async void Read(PacketStream stream)
        {
            var tl = stream.ReadUInt16(); // sid
            stream.ReadUInt16(); // tl; pid
            var objId = stream.ReadBc();

            if (Connection.ActiveChar.ObjId != objId || Connection.ActiveChar.SkillTask == null ||
                Connection.ActiveChar.SkillTask.Skill.TlId != tl)
                return;
            await Connection.ActiveChar.SkillTask.Cancel();
            Connection.ActiveChar.SkillTask.Skill.Stop(Connection.ActiveChar);
        }
    }
}
