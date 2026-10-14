using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.World;

public class WorldEvents
{
    public EventHandler<OnUnitKilledArgs> OnUnitKilled = delegate { };           // IndunEventNpcKilled
    public EventHandler<OnUnitSpawnArgs> OnUnitSpawn = delegate { };             // IndunEventNpcSpawned
    public EventHandler<OnUnitCombatStartArgs> OnUnitCombatStart = delegate { }; // IndunEventNpcCombatStarted
    public EventHandler<OnUnitCombatEndArgs> OnUnitCombatEnd = delegate { };     // IndunEventNpcCombatEnded
    public EventHandler<OnAreaClearArgs> OnAreaClear = delegate { };             // IndunEventNoAliveChInRoom
    public EventHandler<OnDoodadSpawnArgs> OnDoodadSpawn = delegate { };         // IndunEventDoodadSpawned
    public EventHandler<OnDoodadPhaseChangedArgs> OnDoodadPhaseChanged = delegate { }; // IndunEventDoodadPhaseChanged
    public EventHandler<OnIndunDifficultChangedArgs> OnIndunDifficultChanged = delegate { }; // IndunEventDifficultChanged
    /// <summary>
    /// A unit's buff appeared, refreshed or expired. Raised for a copy's units so the indun HUD readouts
    /// (<c>indun_event_npc_info_broadcastings</c>) can report a buff's stack count or remaining time.
    /// </summary>
    public EventHandler<OnUnitBuffChangedArgs> OnUnitBuffChanged = delegate { };
}

/// <summary>One buff edge of a unit: which buff, and whether it is now on the unit.</summary>
public class OnUnitBuffChangedArgs : EventArgs
{
    public Unit Unit { get; set; }
    public uint BuffId { get; set; }

    /// <summary>True when the buff is present after the change (applied/refreshed), false when it left.</summary>
    public bool Present { get; set; }
}

public class OnUnitKilledArgs : EventArgs
{
    public Unit Killer { get; set; }
    public Unit Victim { get; set; }
}

public class OnUnitSpawnArgs : EventArgs
{
    public Unit Npc { get; set; }
}

public class OnUnitCombatStartArgs : EventArgs
{
    public Unit Npc { get; set; }
}

public class OnUnitCombatEndArgs : EventArgs
{
    public Unit Npc { get; set; }
}

public class OnAreaClearArgs : EventArgs
{
}

public class OnDoodadSpawnArgs : EventArgs
{
    public Doodad Doodad { get; set; }
}

public class OnDoodadPhaseChangedArgs : EventArgs
{
    public Doodad Doodad { get; set; }
    /// <summary>The phase the doodad settled on after its phase funcs ran.</summary>
    public uint FuncGroupId { get; set; }

    /// <summary>
    /// The copy's round timer as it stood when the phase change was raised. Read there once, because the
    /// first subscriber's action chain ends the round and every later subscriber must still see it.
    /// </summary>
    public bool RoundTimerRunning { get; set; }
}

public class OnIndunDifficultChangedArgs : EventArgs
{
    /// <summary>CSSelectInstanceDifficultPacket.difficult applied to the copy.</summary>
    public int Difficult { get; set; }
}
