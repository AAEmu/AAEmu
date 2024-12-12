using System;
using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.World;
using NLog;

namespace AAEmu.Game.Core.Managers.World;

public class AreaTriggerManager : Singleton<AreaTriggerManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly List<AreaTrigger> _areaTriggers;
    private List<AreaTrigger> _addQueue;
    private List<AreaTrigger> _removeQueue;

    private object _addLock = new();
    private object _remLock = new();

    public AreaTriggerManager()
    {
        _areaTriggers = [];
        _addQueue = [];
        _removeQueue = [];
    }

    public void Initialize()
    {
        TickManager.Instance.OnTick.Subscribe(Tick, TimeSpan.FromMilliseconds(200), true);
    }

    public void AddAreaTrigger(AreaTrigger trigger)
    {
        trigger.Owner?.AttachAreaTriggers.Add(trigger);
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
                _addQueue = [];
            }

            foreach (var trigger in _areaTriggers)
            {
                // if (trigger.Owner.Position)
                if (trigger?.Owner?.Region?.HasPlayerActivity() ?? false)
                    trigger?.Tick(delta);
            }

            lock (_remLock)
            {
                foreach (var triggerToRemove in _removeQueue)
                {
                    _areaTriggers.Remove(triggerToRemove);
                }

                _removeQueue = [];
            }
        }
        catch (Exception e)
        {
            Logger.Error(e, "Error in AreaTrigger tick !");
        }
    }
}
