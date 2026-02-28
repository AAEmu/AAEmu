namespace AAEmu.Game.Core.Managers;

public interface ITaskManager : IInitializable
{
    bool Cancel(Models.Tasks.Task task);
    void Start();
    void Stop();
    bool Schedule(Models.Tasks.Task task, TimeSpan? startTime = null, TimeSpan? repeatInterval = null, int count = -1);
    bool CronSchedule(Models.Tasks.Task task, string cronExpression, TimeSpan? startDelay = null, int count = -1);
}
