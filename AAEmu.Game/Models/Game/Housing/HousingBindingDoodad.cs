using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.Game.Models.Game.Housing;

public class HousingBindingDoodad
{
    public AttachPointKind AttachPointId { get; set; }
    public uint DoodadId { get; set; }

    /// <summary>
    /// Offset from the house, valid only when <see cref="HasResolvedPosition"/> is set.
    /// </summary>
    public WorldSpawnPosition Position { get; set; }

    /// <summary>
    /// Whether <see cref="Position"/> came from a source that actually defines this attach point.
    /// </summary>
    /// <remarks>
    /// The origin is a legitimate offset, so the coordinate cannot carry this meaning itself: a binding
    /// that genuinely sits at the house origin and one whose attach point was never found would be
    /// indistinguishable. An unresolved binding must not be spawned, saved or realigned, because doing so
    /// writes a position that was never defined and then treats it as authoritative on the next pass.
    /// </remarks>
    public bool HasResolvedPosition { get; set; }
}
