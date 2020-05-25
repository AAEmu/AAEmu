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
            if (NextPhase == 0 && Delay > 0) //This conditional is for doodads that "require" interaction to move on; without interaction
            {
                var nextFunc = DoodadManager.Instance.GetFunc(owner.FuncGroupId, skillId);
                if (nextFunc != null) //Handles examples like not watering a plant or feeding an animal in the given time-frame
                {
                    _log.Warn("DoodadFuncTimer is now calling" + nextFunc.FuncType);
                    nextFunc.Use(caster, owner, skillId);
                }
                else
                {
                    DoodadManager.Instance.TriggerPhaseFunc(GetType().Name, owner.FuncGroupId, caster, owner, skillId);
                }
            }
            else //Start the timers normally
            {
                owner.FuncTask = new DoodadFuncTimerTask(caster, owner, skillId, NextPhase);
                TaskManager.Instance.Schedule(owner.FuncTask, TimeSpan.FromMilliseconds(Delay));
            }
        }
    }
}
