using System;
using AAEmu.Game.Core.Managers;
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
            _log.Debug("DoodadFuncTimer");

            if (Delay > 0)
            {
                owner.GrowthTime = DateTime.Now.AddMilliseconds(Delay); // TODO ... need here?
                owner.FuncTask = new DoodadFuncTimerTask(caster, owner, skillId, NextPhase);
                TaskManager.Instance.Schedule(owner.FuncTask, TimeSpan.FromMilliseconds(Delay));
            }
        }
    }
}
