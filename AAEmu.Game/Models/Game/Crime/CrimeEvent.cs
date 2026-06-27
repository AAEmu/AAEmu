using System.Numerics;

namespace AAEmu.Game.Models.Game.Crime;

public class CrimeEvent
{
    public uint Id { get; set; }
    public uint Criminal { get; set; }
    public uint Victim { get; set; }
    public uint Reporter { get; set; }
    /// <summary>
    /// Source template of the stolen doodad
    /// </summary>
    public uint DoodadTemplate { get; set; }
    public CrimeKind CrimeKind { get; set; } = CrimeKind.Invalid;
    public uint ZoneKey { get; set; }
    public Vector3 Position { get; set; } = Vector3.Zero;
    public DateTime CrimeTime { get; set; } = DateTime.UtcNow;
    public DateTime ReportTime { get; set; } = DateTime.UtcNow;
    public uint UsedSkillId { get; set; }
    public uint DoodadNextFuncGroup { get; set; }
    public uint DoodadFuncId { get; set; }
    public string Msg { get; set; } = string.Empty;
    public DateTime JudgementTime { get; set; } = DateTime.MinValue;
}
