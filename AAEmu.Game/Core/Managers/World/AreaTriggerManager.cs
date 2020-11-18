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
        private List<AreaTrigger> _queuedTriggers;
        private int tickCount;

        public AreaTriggerManager()
        {
            _areaTriggers = new List<AreaTrigger>();
            _queuedTriggers = new List<AreaTrigger>();
            tickCount = 0;
        }
        
        public void Initialize()
        {
            TaskManager.Instance.Schedule(new AreaTriggerTickTask(), TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(50));
        }

        public void AddAreaTrigger(AreaTrigger trigger)
        {
            _queuedTriggers.Add(trigger);
        }

        public void RemoveAreaTrigger(AreaTrigger trigger)
        {
            trigger.OnDelete();
            _areaTriggers.Remove(trigger);
        }
        
        public void Tick()
        {
            if (_queuedTriggers?.Count > 0) 
                _areaTriggers.AddRange(_queuedTriggers);
            _queuedTriggers = new List<AreaTrigger>();
            foreach (var trigger in _areaTriggers)
            {
                trigger.Tick();
            }
        }
    }
}
