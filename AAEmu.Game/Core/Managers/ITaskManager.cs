using System;
using System.Threading.Tasks;

namespace AAEmu.Game.Core.Managers
{
    public interface ITaskManager
    {
        Task<bool> Cancel(Models.Tasks.Task task);
        void Initialize();
        void Schedule(Models.Tasks.Task task, TimeSpan? startTime = null, TimeSpan? repeatInterval = null, int count = -1);
        void Start();
        void Stop();
    }
}
