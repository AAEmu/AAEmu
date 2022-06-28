using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncPuzzleOut : DoodadPhaseFuncTemplate
    {
        public uint GroupId { get; set; }
        public float Ratio { get; set; }
        public string Anim { get; set; }
        public uint ProjectileId { get; set; }
        public uint LootPackId { get; set; }
        public int Delay { get; set; }
        public int NextPhase { get; set; }
        public int ProjectileDelay { get; set; }

        public override bool Use(Unit caster, Doodad owner)
        {
            _log.Trace("DoodadFuncPuzzleOut");
            if (caster is Character)
            {
                //I think this is used to reschedule anything that needs triggered at a specific gametime
                owner.OverridePhase = NextPhase;
                return true;
            }

            return false;
        }
    }
}
