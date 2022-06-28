using AAEmu.Game.Models.Game.Units;
using NLog;

namespace AAEmu.Game.Models.Game.DoodadObj.Templates
{
    public abstract class DoodadPhaseFuncTemplate
    {
        protected static Logger _log = LogManager.GetCurrentClassLogger(); 
        
        public uint Id { get; set; }
        public abstract bool Use(Unit caster, Doodad owner);
    }
}
