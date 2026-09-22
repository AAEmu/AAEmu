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
}

public class OnIndunDifficultChangedArgs : EventArgs
{
    /// <summary>CSSelectInstanceDifficultPacket.difficult applied to the copy.</summary>
    public int Difficult { get; set; }
}
