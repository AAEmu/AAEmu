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

        // The content interval is half-open, matching Random.Next(min, max). A number of final
        // functions deliberately use one fixed respawn time, represented by equal bounds.
        // Do not sample a delay for a non-respawning final phase: that value is never consumed.
        var delay = Respawn ? GetRespawnDelay() : 0;

        if (After > 0)
        {
            var afterTimerDelay = (uint)After;
            if (owner.OverridePhaseTime > DateTime.MinValue)
            {
                owner.PhaseTime = owner.OverridePhaseTime;
                owner.OverridePhaseTime = DateTime.MinValue;
                afterTimerDelay = owner.TimeLeft;
            }

            // Cancel the current task if it exists.
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

            // Create and assign a new task.
            owner.FuncTask = new DoodadFuncFinalTask(caster, owner, 0, Respawn, delay);
            TaskManager.Instance.Schedule(owner.FuncTask, TimeSpan.FromMilliseconds(afterTimerDelay)); // After ms remove the object from visibility
        }
        else
        {
            var spawner = owner.Spawner;
            owner.Delete();
            if (!Respawn)
            {
                // The phase graph is done with this instance; a management spawn hands the placement
                // back to its own percent / min_time / max_time cycle instead of retiring for good.
                spawner?.ScheduleManagementRespawn();
                return false;
            }

            // Cancel the current task if it exists.
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

            // Create and assign a new task.
            owner.FuncTask = new DoodadFuncFinalTask(caster, owner, 0, Respawn, delay);
            TaskManager.Instance.Schedule(owner.FuncTask, TimeSpan.FromMilliseconds(delay));
        }

        return true;
    }

    private int GetRespawnDelay()
    {
        var minimum = Math.Max(0, MinTime);
        var maximum = Math.Max(0, MaxTime);

        // Random.Next requires max > min. Equal values are content's fixed-delay form; retain
        // the lower bound for malformed inverted intervals as well so a bad row cannot strand
        // the doodad's final phase with an exception.
        return maximum <= minimum ? minimum : Random.Shared.Next(minimum, maximum);
    }
}
