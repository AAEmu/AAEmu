using System;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj
{
    public class DoodadFunc
    {
        public uint GroupId { get; set; }
        public uint FuncId { get; set; }
        public string FuncType { get; set; }
        public int NextPhase { get; set; }
        public uint SkillId { get; set; }
        public uint PermId { get; set; }
        public int Count { get; set; }

        public async void Use(Unit caster, Doodad owner, uint skillId)
        {
            owner.GrowthTime = DateTime.MinValue;
            var template = DoodadManager.Instance.GetFuncTemplate(FuncId, FuncType);

            if (template == null)
                return;

            template.Use(caster, owner, skillId);

            if (NextPhase > 0)
            {
                if (owner.FuncTask != null)
                {
                    await owner.FuncTask.Cancel();
                    owner.FuncTask = null;
                }

                owner.FuncGroupId = (uint)NextPhase;
                var funcs = DoodadManager.Instance.GetPhaseFunc(owner.FuncGroupId);
                foreach (var func in funcs)
                    func.Use(caster, owner, skillId);
            }
        }
    }
}
