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
}
