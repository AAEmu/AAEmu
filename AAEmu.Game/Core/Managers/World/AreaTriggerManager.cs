using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.World;
using NLog;

namespace AAEmu.Game.Core.Managers.World;

public class AreaTriggerManager : Singleton<AreaTriggerManager>, IAreaTriggerManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly List<AreaTrigger> _areaTriggers = [];
    private List<AreaTrigger> _addQueue = [];
    private List<AreaTrigger> _removeQueue = [];

    private readonly object _addLock = new();
    private readonly object _remLock = new();

    public void Initialize()
    {
        TickManager.Instance.OnTick.Subscribe(Tick, TimeSpan.FromMilliseconds(200), true);
    }

    /// <summary>
    /// Keep ticking while the doodad's region has players, or leftover occupants still need leave.
    /// </summary>
    public static bool ShouldTick(bool ownerRegionHasPlayers, bool hasOccupants) =>
        ownerRegionHasPlayers || hasOccupants;

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
                if (trigger == null)
                    continue;
                // Idle-region skip is a cost filter. Occupied triggers must still Tick so OnLeave
                // can strip duration-0 clout buffs after the occupant teleports away.
                var ownerBusy = trigger.Owner?.Region?.HasPlayerActivity() ?? false;
                if (ShouldTick(ownerBusy, trigger.HasOccupants))
                    trigger.Tick(delta);
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
