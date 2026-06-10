namespace AAEmu.Game.Models.Game.Events;

/// <summary>
/// Server-driven world event, such as a rift portal, world boss, or invasion.
/// Lifecycle methods may be called by schedulers or GM commands, so
/// implementations should guard their own mutable state.
/// </summary>
public interface IEvent
{
    uint Id { get; set; }
    uint ZoneKey { get; set; }
    uint MapKey { get; set; }

    void Start();
    void Stop();
    void Update();
}
