using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Tasks.Skills;

namespace AAEmu.Game.Core.Managers;

public class EffectTaskManager(ITaskManager taskManager) : Singleton<EffectTaskManager>, IEffectTaskManager
{
    /// <summary>
    /// Pre-Variant of dispel effects...
    /// </summary>
    /// <param name="buff"></param>
    /// <param name="interval">milliseconds</param>
    public void AddDispelTask(Buff buff, double interval)
    {
        var task = new DispelTask(buff);
        taskManager.Schedule(task, TimeSpan.FromMilliseconds(interval)); // TODO create normal effect schedule
    }

    /// <summary>
    /// Schedules the next pulse of an aura buff (<c>aura_radius</c> / <c>aura_slave_buff_id</c>).
    /// </summary>
    /// <param name="buff">The aura instance; the task holds it weakly and stops once it has ended.</param>
    /// <param name="interval">milliseconds</param>
    public void AddAuraTask(Buff buff, double interval)
    {
        var task = new AuraTask(buff);
        taskManager.Schedule(task, TimeSpan.FromMilliseconds(interval));
    }
}
