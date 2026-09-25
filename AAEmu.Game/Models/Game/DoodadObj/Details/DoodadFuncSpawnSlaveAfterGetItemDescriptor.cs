namespace AAEmu.Game.Models.Game.DoodadObj.Details;

/// <summary>
/// Typed, content-only descriptor for <c>doodad_func_spawn_slave_after_get_items</c>.
/// This type intentionally has no runtime grant, spawn, placement, or lifetime behavior.
/// </summary>
public sealed class DoodadFuncSpawnSlaveAfterGetItemDescriptor
{
    public uint Id { get; init; }
    public uint ItemId { get; init; }
    public int Delay { get; init; }
    public float OffsetX { get; init; }
    public float OffsetZ { get; init; }
    public float Angle { get; init; }
}
