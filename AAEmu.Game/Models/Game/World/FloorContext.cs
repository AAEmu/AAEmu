namespace AAEmu.Game.Models.Game.World;

/// <summary>
/// Why floor height is being queried. Used for policy and debug logging.
/// </summary>
public enum FloorContext : byte
{
    Spawn = 0,
    Move = 1,
    Skill = 2,
    Debug = 3
}
