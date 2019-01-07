using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Templates
{
    public abstract class DoodadFuncTemplate
    {
        public uint Id { get; set; }
        public abstract void Use(Unit caster, Doodad owner, uint skillId);
    }
}