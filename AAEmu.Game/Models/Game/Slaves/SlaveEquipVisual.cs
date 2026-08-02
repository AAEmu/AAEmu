namespace AAEmu.Game.Models.Game.Slaves;

/// <summary>
/// Resolved world visual for a slave equipment item (item_slave_equipments / grade_spawns).
/// Exactly one of <see cref="SlaveId"/> or <see cref="DoodadId"/> is non-zero in retail data.
/// </summary>
public readonly struct SlaveEquipVisual(uint slaveId, uint doodadId, float scale)
{
    public uint SlaveId { get; } = slaveId;
    public uint DoodadId { get; } = doodadId;
    public float Scale { get; } = scale;
    public bool IsEmpty => SlaveId == 0 && DoodadId == 0;
}
