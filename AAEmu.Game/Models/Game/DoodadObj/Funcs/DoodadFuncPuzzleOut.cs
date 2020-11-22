using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncPuzzleOut : DoodadFuncTemplate
    {
        public uint GroupId { get; set; }
        public float Ratio { get; set; }
        public string Anim { get; set; }
        public uint ProjectileId { get; set; }
        public uint LootPackId { get; set; }
        public int Delay { get; set; }
        public uint NextPhase { get; set; }
        public int ProjectileDelay { get; set; }
        
        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            _log.Debug("DoodadFuncPuzzleOut");
        }
    }
}
