using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Units;

using NLog;

namespace AAEmu.Game.Models.Game.DoodadObj
{
    public class DoodadPhaseFunc
    {

        private static Logger _log = LogManager.GetCurrentClassLogger();
        public uint GroupId { get; set; }
        public uint FuncId { get; set; }
        public string FuncType { get; set; }

        // This acts as an interface/relay for doodad function chain
        public bool Use(Unit caster, Doodad owner)
        {
            var template = DoodadManager.Instance.GetPhaseFuncTemplate(FuncId, FuncType);
            return template != null && template.Use(caster, owner);
        }
    }
}
