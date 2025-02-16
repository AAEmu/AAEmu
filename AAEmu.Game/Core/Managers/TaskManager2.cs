// Authors: AAGene, ZeromusXYZ
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Tasks;
using NCrontab;

namespace AAEmu.Game.Core.Managers;

// ReSharper disable once ClassNeverInstantiated.Global
public class TaskManager : Singleton<TaskManager>, ITaskManager
{
    private readonly ConcurrentDictionary<uint, Task> _queue = new();
    private readonly HashSet<uint> _taskIds = [];
    private readonly object _taskIdLock = new();
    private uint _taskIdIndex = 1;

    public static readonly CrontabSchedule.ParseOptions s_crontabScheduleParseOptions = new() { IncludingSeconds = true };

    public void Initialize()
    {
        _queue.Clear();
    }

    public void Start()
    {
        TickManager.Instance.OnTick.Subscribe(Tick, TimeSpan.FromMilliseconds(50), true);
    }

    public void Stop()
    {
        // TODO: Wait for still running Tasks before returning
    }

    private void Tick(TimeSpan delta)
    {
        var now = DateTime.UtcNow;
        var toRemove = new List<uint>();
        foreach (var (id, task) in _queue)
        {
            if (task.TriggerTime >= now)
                continue;

            System.Threading.Tasks.Task.Run(task.ExecuteAsync);
            task.ExecuteCount++;

            // Check if there still needs to be executions done
            if ((task.RepeatCount < 0) || (task.ExecuteCount < task.RepeatCount))
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
        if ((startDelay.HasValue && startDelay.Value == TimeSpan.Zero) && (count >= 0) && (count <= 1))
        {
            task.Execute();
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

        return _queue.TryAdd(taskId, task);
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
            task.Execute();
            ReleaseId(task.Id);
            return true;
        }

        var firstPossibleTriggerTime = startDelay.HasValue ? DateTime.UtcNow + startDelay.Value : DateTime.UtcNow;

        task.CronSchedule = CrontabSchedule.Parse(cronExpression, s_crontabScheduleParseOptions);
        task.TriggerTime = task.CronSchedule.GetNextOccurrence(firstPossibleTriggerTime);
        task.RepeatCount = count;

        return _queue.TryAdd(taskId, task);
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
                if (_queue.TryRemove(kvp))
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
