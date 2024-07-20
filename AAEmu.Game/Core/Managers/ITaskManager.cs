using System;
using System.Threading.Tasks;

namespace AAEmu.Game.Core.Managers;

public interface ITaskManager
{
    bool Cancel(Models.Tasks.Task task);
    void Initialize();
    void Start();
    void Stop();
    bool Schedule(Models.Tasks.Task task, TimeSpan? startTime = null, TimeSpan? repeatInterval = null, int count = -1);
    bool CronSchedule(Models.Tasks.Task task, string cronExpression, TimeSpan? startDelay = null, int count = -1);
}
