namespace AAEmu.Game.Models.Game.World;

/// <summary>
/// Diagnostic tag for floor queries (FloorDebug logs, GM /height).
/// Does not change which provider wins — only <see cref="FloorPolicyMode"/> and world data do.
/// </summary>
public enum FloorContext : byte
{
    Spawn = 0,
    Move = 1,
    Skill = 2,
    Debug = 3
}
