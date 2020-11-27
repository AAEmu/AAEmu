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
        private List<AreaTrigger> _addQueue;
        private List<AreaTrigger> _removeQueue;

        public AreaTriggerManager()
        {
            _areaTriggers = new List<AreaTrigger>();
            _addQueue = new List<AreaTrigger>();
            _removeQueue = new List<AreaTrigger>();
        }
        
        public void Initialize()
        {
            TickManager.Instance.OnTick += Tick;
        }

        public void AddAreaTrigger(AreaTrigger trigger)
        {
            _addQueue.Add(trigger);
        }

        public void RemoveAreaTrigger(AreaTrigger trigger)
        {
            trigger.OnDelete();
            _removeQueue.Add(trigger);
        }
        
        public void Tick()
        {
            if (_addQueue?.Count > 0) 
                _areaTriggers.AddRange(_addQueue);
            _addQueue = new List<AreaTrigger>();
            foreach (var trigger in _areaTriggers)
            {
                trigger?.Tick();
            }

            foreach (var triggerToRemove in _removeQueue)
            {
                _areaTriggers.Remove(triggerToRemove);
            }
            _removeQueue = new List<AreaTrigger>();
        }
    }
}
