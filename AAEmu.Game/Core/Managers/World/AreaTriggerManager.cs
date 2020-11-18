using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Tasks.AreaTriggers;

namespace AAEmu.Game.Core.Managers.World
{
    public class AreaTriggerManager : Singleton<AreaTriggerManager>
    {
        private readonly List<AreaTrigger> _areaTriggers;
        private int tickCount;

        public AreaTriggerManager()
        {
            _areaTriggers = new List<AreaTrigger>();
            tickCount = 0;
        }
        
        public void Initialize()
        {
            TaskManager.Instance.Schedule(new AreaTriggerTickTask(), TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(50));
        }

        public void AddAreaTrigger(AreaTrigger trigger)
        {
            _areaTriggers.Add(trigger);
        }

        public void RemoveAreaTrigger(AreaTrigger trigger)
        {
            trigger.OnDelete();
            _areaTriggers.Remove(trigger);
        }
        
        public void Tick()
        {
            foreach (var trigger in _areaTriggers)
            {
                trigger.Tick();
            }
        }
    }
}
