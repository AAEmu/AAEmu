using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Units
{
    public sealed class Portal : Npc
    {
        public override UnitCustomModelParams ModelParams { get; set; }
        
        public Transform TeleportPosition { get; set; }

        public Portal()
        {
            ModelParams = new UnitCustomModelParams();
        }
    }
}
