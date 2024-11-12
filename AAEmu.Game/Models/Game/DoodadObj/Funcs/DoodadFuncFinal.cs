using System;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Doodads;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncFinal : DoodadPhaseFuncTemplate
{
    public int After { get; set; }
    public bool Respawn { get; set; }
    public int MinTime { get; set; }
    public int MaxTime { get; set; }
    public bool ShowTip { get; set; }
    public bool ShowEndTime { get; set; }
    public string Tip { get; set; }

    public override bool Use(BaseUnit caster, Doodad owner)
    {
        if (caster is Character)
            Logger.Debug("DoodadFuncFinal: After {0}, Respawn {1}, MinTime {2}, MaxTime {3}, ShowTip {4}, ShowEndTime {5}, Tip {6}", After, Respawn, MinTime, MaxTime, ShowTip, ShowEndTime, Tip);
        else
            Logger.Trace("DoodadFuncFinal: After {0}, Respawn {1}, MinTime {2}, MaxTime {3}, ShowTip {4}, ShowEndTime {5}, Tip {6}", After, Respawn, MinTime, MaxTime, ShowTip, ShowEndTime, Tip);

        var delay = Rand.Next(MinTime, MaxTime);

        if (After > 0)
        {
            var afterTimerDelay = (uint)After;
            if (owner.OverridePhaseTime > DateTime.MinValue)
            {
                owner.PhaseTime = owner.OverridePhaseTime;
                owner.OverridePhaseTime = DateTime.MinValue;
                afterTimerDelay = owner.TimeLeft;
            }
            
            // Отменяем текущую задачу, если она существует
            // Cancel the current task if it exists
            if (owner.FuncTask != null)
            {
                try
                {
                    TaskManager.Instance.Cancel(owner.FuncTask);
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to cancel existing FuncTask: {0}", ex.Message);
                }
            }

            // Создаем и назначаем новую задачу
            // Create and assign a new task
            owner.FuncTask = new DoodadFuncFinalTask(caster, owner, 0, Respawn, delay);
            TaskManager.Instance.Schedule(owner.FuncTask, TimeSpan.FromMilliseconds(afterTimerDelay)); // After ms remove the object from visibility
        }
        else
        {
            owner.Delete();
            if (!Respawn) { return false; }

            // Отменяем текущую задачу, если она существует
            // Cancel the current task if it exists
            if (owner.FuncTask != null)
            {
                try
                {
                    TaskManager.Instance.Cancel(owner.FuncTask);
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to cancel existing FuncTask: {0}", ex.Message);
                }
            }

            // Создаем и назначаем новую задачу
            // Create and assign a new task
            owner.FuncTask = new DoodadFuncFinalTask(caster, owner, 0, Respawn, delay);
            TaskManager.Instance.Schedule(owner.FuncTask, TimeSpan.FromMilliseconds(delay));
        }

        return true;
    }
}
