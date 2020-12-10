using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Tasks.AreaTriggers;
using NLog;

namespace AAEmu.Game.Core.Managers.World
{
    public class AreaTriggerManager : Singleton<AreaTriggerManager>
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();
        
        private readonly List<AreaTrigger> _areaTriggers;
        private List<AreaTrigger> _addQueue;
        private List<AreaTrigger> _removeQueue;
        
        private object _addLock = new object();
        private object _remLock = new object();

        public AreaTriggerManager()
        {
            _areaTriggers = new List<AreaTrigger>();
            _addQueue = new List<AreaTrigger>();
            _removeQueue = new List<AreaTrigger>();
        }
        
        public void Initialize()
        {
            TickManager.Instance.OnTick.Subscribe(Tick, TimeSpan.FromMilliseconds(200));
        }

        public void AddAreaTrigger(AreaTrigger trigger)
        {
            lock (_addLock)
            {
                _addQueue.Add(trigger);
            }
        }

        public void RemoveAreaTrigger(AreaTrigger trigger)
        {
            trigger.OnDelete();
            lock (_remLock)
            {
                _removeQueue.Add(trigger);
            }
        }
        
        public void Tick(TimeSpan delta)
        {
            try
            {
                lock (_addLock)
                {
                    if (_addQueue?.Count > 0)
                        _areaTriggers.AddRange(_addQueue);
                    _addQueue = new List<AreaTrigger>();
                }

                foreach (var trigger in _areaTriggers)
                {
                    trigger?.Tick(delta);
                }

                lock (_remLock)
                {
                    foreach (var triggerToRemove in _removeQueue)
                    {
                        _areaTriggers.Remove(triggerToRemove);
                    }

                    _removeQueue = new List<AreaTrigger>();
                }
            }
            catch (Exception e)
            {
                _log.Error(e, "Error in AreaTrigger tick !");
            }
        }
    }
}
