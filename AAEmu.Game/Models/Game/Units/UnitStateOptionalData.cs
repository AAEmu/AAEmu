namespace AAEmu.Game.Models.Game.Units;

/// <summary>
/// </summary>
public sealed class UnitStateOptionalData
{
    public int PlotDebugLevel { get; set; }

    /// <summary>
    /// </summary>
    public int VersionReserved7Fb0 { get; set; }

    /// <summary>
    /// </summary>
    public int VersionReserved7Fb4 { get; set; }

    public uint FirstHitterObjectId { get; set; }
    public int FirstHitterTeamId { get; set; }
    public ushort[] HighAbilityValues { get; } = new ushort[4];
    public sbyte[] GmModeValues { get; } = new sbyte[9];
}
