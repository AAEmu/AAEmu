namespace AAEmu.Game.Models.Spheres;

public class SphereBuffs
{
    public uint Id { get; set; }
    public uint BuffId { get; set; }
    /// <summary>Buff removed on leave (often same as <see cref="BuffId"/>).</summary>
    public uint RemoveOnLeaveBuffId { get; set; }
    public bool AndPet { get; set; }
}
