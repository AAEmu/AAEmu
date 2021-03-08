using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Units
{
    public sealed class Portal : Npc
    {
        public override UnitCustomModelParams ModelParams { get; set; }
        
        public Point TeleportPosition { get; set; }

        public Portal()
        {
            ModelParams = new UnitCustomModelParams();
        }

        public override void Delete()
        {
            BroadcastPacket(new SCUnitDeathPacket(ObjId, KillReason.PortalTimeout), false);
            base.Delete();
        }
    }
}
