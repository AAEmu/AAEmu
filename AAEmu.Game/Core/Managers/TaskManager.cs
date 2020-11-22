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
    public class TaskManager : Singleton<TaskManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private DefaultThreadPool _generalPool;
        private IScheduler _generalScheduler;

        public async void Initialize()
        {
            _generalPool = new DefaultThreadPool();
            _generalPool.MaxConcurrency = AppConfiguration.Instance.MaxConcurencyThreadPool;
            _generalPool.Initialize();

            DirectSchedulerFactory
                .Instance
                .CreateScheduler("General Scheduler", "GeneralScheduler", _generalPool, new RAMJobStore());
            _generalScheduler = await DirectSchedulerFactory.Instance.GetScheduler("General Scheduler");
        }

        public void Start()
        {
            _generalScheduler.Start();
        }

        public void Stop()
        {
            _generalScheduler.Shutdown(true);
        }

        public async void Schedule(Task task, TimeSpan? startTime = null, TimeSpan? repeatInterval = null,
            int count = -1)
        {
            if (_generalScheduler.IsShutdown)
                return;

            task.Id = TaskIdManager.Instance.GetNextId();
            while (await _generalScheduler.CheckExists(new JobKey(task.Name + task.Id, task.Name)))
                task.Id = TaskIdManager.Instance.GetNextId();
            
            var job = JobBuilder
                .Create<TaskJob>()
                .WithIdentity(task.Name + task.Id, task.Name)
                .Build();
            job.JobDataMap.Put("Logger", _log);
            job.JobDataMap.Put("Task", task);
            task.JobDetail = job;

            var triggerBuild = TriggerBuilder
                .Create()
                .WithIdentity(job.Key.Name, job.Key.Group);

            if (startTime == null)
                triggerBuild.StartNow();
            else
                triggerBuild.StartAt(DateTime.Now.Add((TimeSpan) startTime));

            if (task.Scheduler == null)
            {
                triggerBuild.WithSimpleSchedule(scheduler =>
                {
                    if (repeatInterval == null)
                        return;

                    scheduler.WithInterval((TimeSpan) repeatInterval);

                    if (count > 0)
                        scheduler.WithRepeatCount(count);
                    else if (count == -1)
                        scheduler.RepeatForever();
                });
            }
            else
                triggerBuild.WithSchedule(task.Scheduler);

            task.Trigger = triggerBuild.Build();
            task.ExecuteCount = 0;
            task.MaxCount = count;
            task.ScheduleTime = Helpers.UnixTimeNowInMilli();

            await _generalScheduler.ScheduleJob(job, task.Trigger);
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
                _log.Warn(e);
            }

            return task.Cancelled;
        }
    }

    [PersistJobDataAfterExecution]
    public sealed class TaskJob : IJob
    {
        public ThreadTask Execute(IJobExecutionContext context)
        {
            var log = (Logger) context.MergedJobDataMap.Get("Logger");
            try
            {
                var task = (Task) context.MergedJobDataMap.Get("Task");
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
                TaskIdManager.Instance.ReleaseId((uint) id)
            );
            thread.Start(taskId);
        }
    }
}
