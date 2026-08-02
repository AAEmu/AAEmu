// Authors: AAGene, ZeromusXYZ

using System.Collections.Concurrent;
using AAEmu.Commons.Utils;
using NCrontab;
using NLog;
using Task = AAEmu.Game.Models.Tasks.Task;
using AsyncTask = System.Threading.Tasks.Task;

namespace AAEmu.Game.Core.Managers;

// ReSharper disable once ClassNeverInstantiated.Global
public class TaskManager(ITickManager tickManager) : Singleton<TaskManager>, ITaskManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private readonly ConcurrentDictionary<uint, Task> _queue = new();
    private readonly ConcurrentDictionary<long, AsyncTask> _runningTasks = new();
    private readonly HashSet<uint> _taskIds = [];
    private readonly object _taskIdLock = new();
    private readonly object _lifecycleLock = new();
    private uint _taskIdIndex = 1;
    private long _executionId;
    private bool _stopping;

    public static readonly CrontabSchedule.ParseOptions s_crontabScheduleParseOptions = new() { IncludingSeconds = true };

    public void Initialize()
    {
        // NOTE: _queue is intentionally NOT cleared here. In the orchestrated boot,
        // ILoadable.Load() runs in Stage 2 and may schedule tasks (e.g. ZoneManager
        // queues the initial Conflict→War→Peace timer chain). Initialize() runs in
        // Stage 4, AFTER those tasks are already queued — clearing the queue here
        // wiped the boot-time tasks and broke the zone-conflict cycle.
        // The queue is already empty after the field initializer, so no clear needed.
    }

    public void Start()
    {
        lock (_lifecycleLock)
            _stopping = false;

        tickManager.OnTick.Subscribe(Tick, TimeSpan.FromMilliseconds(50), true);
    }

    public async AsyncTask StopAsync(CancellationToken cancellationToken)
    {
        AsyncTask[] runningTasks;
        lock (_lifecycleLock)
        {
            _stopping = true;
            runningTasks = _runningTasks.Values.ToArray();
        }

        foreach (var (taskId, task) in _queue.ToArray())
        {
            if (!_queue.TryRemove(taskId, out _))
                continue;

            task.Cancelled = true;
            ReleaseId(taskId);
        }

        if (runningTasks.Length == 0)
            return;

        try
        {
            await AsyncTask.WhenAll(runningTasks).WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Logger.Warn("Task manager shutdown cancelled with {0} task executions still running", _runningTasks.Count);
        }
    }

    private void Tick(TimeSpan delta)
    {
        var now = DateTime.UtcNow;
        var toRemove = new List<uint>();
        foreach (var (id, task) in _queue)
        {
            if (task.TriggerTime >= now)
                continue;

            lock (_lifecycleLock)
            {
                if (_stopping)
                    return;

                var executionId = Interlocked.Increment(ref _executionId);
                var execution = AsyncTask.Run(() => ExecuteTrackedAsync(task));
                _runningTasks.TryAdd(executionId, execution);
                _ = RemoveCompletedExecutionAsync(executionId, execution);
            }
            task.ExecuteCount++;

            // Check if there still needs to be executions done
            if (task.RepeatCount < 0 || task.ExecuteCount < task.RepeatCount)
            {
                // If there is a CronSchedule set, use that to calculate the next TriggerTime
                if (task.CronSchedule != null)
                    task.TriggerTime = task.CronSchedule.GetNextOccurrence(now);

                // If there is an interval set, add it for the next TriggerTime
                if (task.RepeatInterval != TimeSpan.Zero)
                    task.TriggerTime = now + task.RepeatInterval;

                continue; // Don't remove this Task from the queue yet
            }

            toRemove.Add(id);
        }

        foreach (var objId in toRemove)
        {
            _queue.Remove(objId, out _);
            ReleaseId(objId);
        }
    }

    private static async AsyncTask ExecuteTrackedAsync(Task task)
    {
        try
        {
            await task.ExecuteAsync();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Scheduled task {0} ({1}) failed", task.Id, task.Name);
        }
    }

    private async AsyncTask RemoveCompletedExecutionAsync(long executionId, AsyncTask execution)
    {
        await execution;
        _runningTasks.TryRemove(executionId, out _);
    }

    /// <summary>
    /// Schedules a task to be executed in the future
    /// </summary>
    /// <param name="task">Task to Execute</param>
    /// <param name="startDelay">First trigger is startDelay time from now</param>
    /// <param name="repeatInterval">Time between Task Executions, needs to be set to allow usage of count</param>
    /// <param name="count">Number of times to repeat this action, -1 means infinite and 0 is the same as 1 time</param>
    /// <returns></returns>
    public bool Schedule(Task task, TimeSpan? startDelay = null, TimeSpan? repeatInterval = null, int count = -1)
    {
        var taskId = NextId();
        task.Id = taskId;

        // If it's only supposed to run once and immediately, then don't queue it, and just run now
        if (startDelay.HasValue && startDelay.Value == TimeSpan.Zero && count >= 0 && count <= 1)
        {
            lock (_lifecycleLock)
            {
                if (_stopping)
                {
                    ReleaseId(task.Id);
                    return false;
                }

                task.Execute();
            }
            ReleaseId(task.Id);
            return true;
        }

        task.TriggerTime = startDelay.HasValue ? DateTime.UtcNow + startDelay.Value : DateTime.UtcNow;

        if (repeatInterval.HasValue)
        {
            task.RepeatInterval = repeatInterval.Value;
            task.RepeatCount = count;
        }
        else
        {
            task.RepeatCount = 1;
        }

        lock (_lifecycleLock)
        {
            if (_stopping || !_queue.TryAdd(taskId, task))
            {
                ReleaseId(taskId);
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Schedules a task to be executed in the future
    /// </summary>
    /// <param name="task">Task to Execute</param>
    /// <param name="cronExpression">Cron expression that defines the trigger conditions</param>
    /// <param name="startDelay">First trigger is only possible startDelay time from now</param>
    /// <param name="count">Number of times to repeat this action, -1 means infinite and 0 is the same as 1 time</param>
    /// <returns></returns>
    public bool CronSchedule(Task task, string cronExpression, TimeSpan? startDelay = null, int count = -1)
    {
        var taskId = NextId();
        task.Id = taskId;

        if (startDelay.HasValue && startDelay.Value == TimeSpan.Zero)
        {
            lock (_lifecycleLock)
            {
                if (_stopping)
                {
                    ReleaseId(task.Id);
                    return false;
                }

                task.Execute();
            }
            ReleaseId(task.Id);
            return true;
        }

        var firstPossibleTriggerTime = startDelay.HasValue ? DateTime.UtcNow + startDelay.Value : DateTime.UtcNow;

        task.CronSchedule = CrontabSchedule.Parse(cronExpression, s_crontabScheduleParseOptions);
        task.TriggerTime = task.CronSchedule.GetNextOccurrence(firstPossibleTriggerTime);
        task.RepeatCount = count;

        lock (_lifecycleLock)
        {
            if (_stopping || !_queue.TryAdd(taskId, task))
            {
                ReleaseId(taskId);
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Cancels a Task
    /// </summary>
    /// <param name="task"></param>
    /// <returns></returns>
    public bool Cancel(Task task)
    {
        var res = _queue.Remove(task.Id, out _);

        if (res)
        {
            task.Cancelled = true;
            ReleaseId(task.Id);
        }

        return res;
    }
    public void RemoveTasks(Func<Task, bool> predicate)
    {
        // Take a snapshot of the current tasks to avoid modifying the collection while iterating.
        foreach (var kvp in _queue.ToArray())
        {
            if (predicate(kvp.Value))
            {
                _queue.Remove(kvp.Key, out _);
                ReleaseId(kvp.Key);
            }
        }
    }
    private uint NextId()
    {
        lock (_taskIdLock)
        {
            var id = _taskIdIndex;
            while (_taskIds.Contains(id))
            {
                if (id == uint.MaxValue)
                    id = 1;
                else
                    id++;
            }
            _taskIds.Add(id);
            _taskIdIndex = id + 1u;
            if (_taskIdIndex == 0)
                _taskIdIndex = 1;

            return id;
        }
    }

    private void ReleaseId(uint id)
    {
        lock (_taskIdLock)
        {
            _taskIds.Remove(id);
        }
    }

    public int GetQueueCount()
    {
        return _queue.Count;
    }
}
