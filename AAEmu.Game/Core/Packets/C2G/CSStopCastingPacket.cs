using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Tasks.Skills;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSStopCastingPacket : GamePacket
    {
        public CSStopCastingPacket() : base(CSOffsets.CSStopCastingPacket, 1)
        {
        }

        public override async void Read(PacketStream stream)
        {
            var tl = stream.ReadUInt16(); // sid
            var pid = stream.ReadUInt16(); // tl; pid
            var objId = stream.ReadBc();

            if (Connection.ActiveChar.ObjId != objId)
                return;
            if (pid != 0)
            {
                if(Connection.ActiveChar.ActivePlotState.ActiveSkill.TlId == pid)
                {
                    Connection.ActiveChar.ActivePlotState.RequestCancellation();
                }
                else
                {
                    Connection.SendPacket(new SCPlotCastingStoppedPacket(pid, 0, 1));
                    Connection.SendPacket(new SCPlotChannelingStoppedPacket(pid, 0, 1));
                }
            }
            if (Connection.ActiveChar.SkillTask == null || Connection.ActiveChar.SkillTask.Skill.TlId != tl)
                return;
            
            await Connection.ActiveChar.SkillTask.Cancel();

            if (Connection.ActiveChar.SkillTask is EndChannelingTask ect)
            {
                Connection.ActiveChar.SkillTask.Skill.Stop(Connection.ActiveChar, ect._channelDoodad);
            }
            else
            {
                Connection.ActiveChar.SkillTask.Skill.Stop(Connection.ActiveChar);
            }
        }
    }
}
