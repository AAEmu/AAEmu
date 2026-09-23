namespace AAEmu.Game.Models.Game.Residents;

/// <summary>One <c>local_development_boards</c> notice. <c>show_text</c> is not a contribution threshold.</summary>
public sealed record LocalDevelopmentBoardRow(uint RowId, uint BoardTypeId, uint ShowPhase, uint? Threshold);

/// <summary>One <c>local_developments</c> row joined with its board notices.</summary>
public sealed class LocalDevelopmentDefinition
{
    public uint Id { get; init; }
    public ushort ZoneGroupId { get; init; }
    public uint DoodadAlmightyId { get; init; }
    public uint BoardDoodadId { get; init; }

    /// <summary>Raw <c>doodad_phase_0..3</c> func-group ids; a value &lt;= 0 means the content did not define it.</summary>
    public int[] DoodadPhases { get; init; } = [];

    public List<LocalDevelopmentBoardRow> BoardRows { get; } = [];
}
