using AAEmu.Game.Models.Game.Units;

using NLog;

namespace AAEmu.Game.Models.Game.DoodadObj.Templates
{
    public abstract class DoodadFuncTemplate
    {
        protected static Logger _log = LogManager.GetCurrentClassLogger();

        public uint Id { get; set; }
        public abstract void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0);
    }
}
