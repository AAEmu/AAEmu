using System.Threading.Tasks;
using AAEmu.Game.Core.Managers;
using Quartz;

namespace AAEmu.Game.Models.Tasks
{
    public abstract class Task
    {
        public uint Id { get; set; }
        public string Name { get; set; }
        public IScheduleBuilder Scheduler { get; set; } = null;
        public IJobDetail JobDetail { get; set; }
        public ITrigger Trigger { get; set; }
        public bool Cancelled { get; set; }
        public long ScheduleTime { get; set; }
        public int MaxCount { get; set; }
        public int ExecuteCount { get; set; }

        protected Task()
        {
            Name = GetType().Name;
            Cancelled = false;
        }

        public abstract void Execute();

        public async Task<bool> CancelAsync()
        {
            var result = await TaskManager.Instance.Cancel(this);
            if (result)
            {
                OnCancel();
                return true;
            }

            return false;
        }

        public virtual void OnCancel()
        {
        }
    }
}
