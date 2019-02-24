using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Doodads;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncGrowth : DoodadFuncTemplate
    {
        public int Delay { get; set; }
        public int StartScale { get; set; }
        public int EndScale { get; set; }
        public uint NextPhase { get; set; }

        public override void Use(Unit caster, Doodad owner, uint skillId)
        {
            _log.Debug("DoodadFuncGrowth");
            if (Delay > 0)
            {
                owner.GrowthTime = DateTime.Now.AddMilliseconds(Delay);
                owner.FuncTask = new DoodadFuncGrowthTask(caster, owner, skillId, NextPhase);
                TaskManager.Instance.Schedule(owner.FuncTask, TimeSpan.FromMilliseconds(Delay));
            }
        }
    }
}
