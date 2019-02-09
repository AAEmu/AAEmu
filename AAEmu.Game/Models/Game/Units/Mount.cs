using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Models.Game.Units
{
    public sealed class Mount : Npc
    {
        public ushort TlId { get; set; }
        public override UnitCustomModelParams ModelParams { get; set; }

        public Mount()
        {
            ModelParams = new UnitCustomModelParams();
        }
    }
}
