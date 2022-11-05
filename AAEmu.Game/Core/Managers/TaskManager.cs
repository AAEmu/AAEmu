using System;
using System.Threading;
using System.Threading.Tasks;
using ThreadTask = System.Threading.Tasks.Task;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Models;
using NLog;
using Quartz;
using Quartz.Impl;
using Quartz.Simpl;
using Task = AAEmu.Game.Models.Tasks.Task;

namespace AAEmu.Game.Core.Managers
{
    public class TaskManager : Singleton<TaskManager>, ITaskManager
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        private bool _initialized = false;

        private DefaultThreadPool _generalPool;
        private IScheduler _generalScheduler;

        public async void Initialize()
        {
            if (_initialized)
                return;

            _generalPool = new DefaultThreadPool();
            _generalPool.MaxConcurrency = AppConfiguration.Instance.MaxConcurencyThreadPool;
            _generalPool.Initialize();

            DirectSchedulerFactory
                .Instance
                .CreateScheduler("General Scheduler", "GeneralScheduler", _generalPool, new RAMJobStore());
            _generalScheduler = await DirectSchedulerFactory.Instance.GetScheduler("General Scheduler");
            _initialized = true;
        }

        public void Start()
        {
            _generalScheduler.Start();
        }

        public void Stop()
        {
            _generalScheduler?.Shutdown(true);
        }

        public async void Schedule(Task task, TimeSpan? startTime = null, TimeSpan? repeatInterval = null, int count = -1)
        {
            if (_generalScheduler.IsShutdown)
                return;

            if (task == null)
            {
                _log.Error("Task.Schedule: Task is NULL !!! StartTime: {0}, repeatInterval: {1}, count: {2}", startTime, repeatInterval, count);
                return;
            }

            task.Id = TaskIdManager.Instance.GetNextId();
            while (await _generalScheduler.CheckExists(new JobKey(task.Name + task.Id, task.Name)))
                task.Id = TaskIdManager.Instance.GetNextId();

            IJobDetail job;
            var newJob = task.JobDetail == null;
            if (newJob)
            {
                job = JobBuilder
                    .Create<TaskJob>()
                    .WithIdentity(task.Name + task.Id, task.Name)
                    .Build();
                job.JobDataMap.Put("Logger", _log);
                job.JobDataMap.Put("Task", task);
                task.JobDetail = job;
            }

            var triggerBuild = TriggerBuilder
                .Create()
                .WithIdentity(task.JobDetail.Key.Name, task.JobDetail.Key.Group);

            if (startTime == null)
                triggerBuild.StartNow();
            else
                triggerBuild.StartAt(DateTime.UtcNow.Add((TimeSpan)startTime));

            if (task.Scheduler == null)
            {
                triggerBuild.WithSimpleSchedule(scheduler =>
                {
                    if (repeatInterval == null)
                        return;

                    scheduler.WithInterval((TimeSpan)repeatInterval);

                    if (count > 0)
                        scheduler.WithRepeatCount(count);
                    else if (count == -1)
                        scheduler.RepeatForever();
                });
            }
            else
                triggerBuild.WithSchedule(task.Scheduler);

            triggerBuild.ForJob(task.JobDetail.Key);

            task.Trigger = triggerBuild.Build();
            task.ExecuteCount = 0;
            task.MaxCount = repeatInterval == null ? 0 : count;
            task.ScheduleTime = Helpers.UnixTimeNowInMilli();

            try
            {
                if (newJob)
                {
                    try
                    {
                        await _generalScheduler.ScheduleJob(task.JobDetail, task.Trigger);
                    }
                    catch (Exception e)
                    {
                        _log.Trace(e, "Rescheduling task");
                        try
                        {
                            await _generalScheduler.RescheduleJob(task.Trigger.Key, task.Trigger);
                        }
                        catch (Exception exception)
                        {
                            _log.Error(exception, "Error scheduling task");
                        }
                    }
                }
                else
                {
                    try
                    {
                        await _generalScheduler.RescheduleJob(task.Trigger.Key, task.Trigger);
                    }
                    catch (Exception e)
                    {
                        _log.Error(e, "Error scheduling task");
                    }
                }
            }
            catch (Exception e)
            {
                _log.Error(e, "Error scheduling task");
            }
        }

        public async void CronSchedule(Task task, string cronExpression, TimeSpan? startTime = null, TimeSpan? repeatInterval = null, int count = -1)
        {
            if (_generalScheduler.IsShutdown)
                return;

            if (task == null)
            {
                _log.Error("Task.Schedule: Task is NULL !!! StartTime: {0}, repeatInterval: {1}, count: {2}", startTime, repeatInterval, count);
                return;
            }

            //var _cron = "0 0 22-7 * * *";
            task.Id = TaskIdManager.Instance.GetNextId();
            while (await _generalScheduler.CheckExists(new JobKey(task.Name + task.Id, task.Name)))
                task.Id = TaskIdManager.Instance.GetNextId();

            IJobDetail job;
            var newJob = task.JobDetail == null;
            if (newJob)
            {
                job = JobBuilder
                    .Create<TaskJob>()
                    .WithIdentity(task.Name + task.Id, task.Name)
                    .Build();
                job.JobDataMap.Put("Logger", _log);
                job.JobDataMap.Put("Task", task);
                task.JobDetail = job;
            }

            var triggerBuild = TriggerBuilder
                .Create()
                .WithIdentity(task.JobDetail.Key.Name, task.JobDetail.Key.Group);

            if (startTime == null)
                triggerBuild.StartNow();
            else
                triggerBuild.StartAt(DateTime.UtcNow.Add((TimeSpan)startTime));

            if (task.Scheduler == null)
            {
                triggerBuild.WithCronSchedule(cronExpression);
            }
            else
                triggerBuild.WithSchedule(CronScheduleBuilder.CronSchedule(cronExpression));

            triggerBuild.ForJob(task.JobDetail.Key);
            task.Trigger = triggerBuild.Build();

            task.ExecuteCount = 0;
            task.MaxCount = repeatInterval == null ? 0 : count;
            task.ScheduleTime = Helpers.UnixTimeNowInMilli();

            try
            {
                if (newJob)
                {
                    await _generalScheduler.ScheduleJob(task.JobDetail, task.Trigger);
                }
                else
                {
                    await _generalScheduler.RescheduleJob(task.Trigger.Key, task.Trigger);
                }
            }
            catch (Exception e)
            {
                _log.Error(e, "Error cron scheduling task");
            }
        }

        public async Task<bool> Cancel(Task task)
        {
            if (task?.JobDetail == null)
                return true;
            try
            {
                var result = await _generalScheduler.DeleteJob(task.JobDetail.Key);
                if (result)
                {
                    task.Cancelled = true;

                    TaskIdManager.Instance.ReleaseId(task.Id);
                }

                return result;
            }
            catch (SchedulerException e)
            {
                _log.Warn(e, "Error canceling task");
            }

            return task.Cancelled;
        }
    }

    [DisallowConcurrentExecution]
    [PersistJobDataAfterExecution]
    public sealed class TaskJob : IJob
    {
        public ThreadTask Execute(IJobExecutionContext context)
        {
            var log = (Logger)context.MergedJobDataMap.Get("Logger");
            try
            {
                var task = (Task)context.MergedJobDataMap.Get("Task");
                if (task.Cancelled)
                    return ThreadTask.CompletedTask;

                task.Execute();
                task.ExecuteCount++;

                if (task.MaxCount != -1 && task.ExecuteCount > task.MaxCount)
                    Clear(task.Id);
            }
            catch (Exception e)
            {
                log.Error(e);
            }

            return ThreadTask.CompletedTask;
        }

        private void Clear(uint taskId)
        {
            var thread = new Thread(id =>
                TaskIdManager.Instance.ReleaseId((uint)id)
            );
            thread.Start(taskId);
        }
    }
}
