using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Doodads;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncTimer : DoodadFuncTemplate
    {
        public int Delay { get; set; }
        public uint NextPhase { get; set; }
        public bool KeepRequester { get; set; }
        public bool ShowTip { get; set; }
        public bool ShowEndTime { get; set; }
        public string Tip { get; set; }

        public override void Use(Unit caster, Doodad owner, uint skillId)
        {
            //_log.Debug("Delay " + Delay);
            //_log.Debug("NextPhase " + NextPhase);
            //_log.Debug("KeepRequester " + KeepRequester);
            //_log.Debug("ShowTip " + ShowTip);
            //_log.Debug("ShowEndTime " + ShowEndTime);
            //_log.Debug("Tip " + Tip);

            owner.GrowthTime = DateTime.Now.AddMilliseconds(Delay + 1); // TODO need here

            if (NextPhase > 0)
            {
                owner.FuncTask = new DoodadFuncTimerTask(caster, owner, skillId, NextPhase);
                TaskManager.Instance.Schedule(owner.FuncTask, TimeSpan.FromMilliseconds(Delay + 1));
            }
            else
            {
                //Wondering if more needs done here if depending on next phase func
                DoodadManager.Instance.TriggerPhases(GetType().Name, caster, owner, skillId);
            }
        }
    }
}
