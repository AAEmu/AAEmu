using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Units;
using NLog;

namespace AAEmu.Game.Models.Game.DoodadObj
{
    public class DoodadFunc
    {

        private static Logger _log = LogManager.GetCurrentClassLogger();
        public uint GroupId { get; set; }
        public uint FuncId { get; set; }
        public uint FuncKey { get; set; }
        public string FuncType { get; set; }
        public int NextPhase { get; set; }
        public uint SoundId { get; set; }
        public uint SkillId { get; set; }
        public uint PermId { get; set; }
        public int Count { get; set; }

        //This acts as an interface/relay for doodad function chain
        public async void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {            
            var template = DoodadManager.Instance.GetFuncTemplate(FuncId, FuncType);
            if (template == null)
                return;
         
            template.Use(caster, owner, skillId, nextPhase);
            // if (NextPhase > 0)
            // {
            //     //Queue the next phase
            //     var next = DoodadManager.Instance.GetFunc((uint)NextPhase, 0);
            //     if (next == null)
            //         return;
            //     next.Use(caster, owner, skillId);
            // }
        }
    }
}
