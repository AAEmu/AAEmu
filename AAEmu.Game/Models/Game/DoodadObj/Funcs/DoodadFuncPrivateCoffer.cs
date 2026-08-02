namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

/// <summary>
/// Content-driven private coffer configuration. Native actual doodad function type 0x35 maps to
/// this table and exposes the <see cref="IsManikin"/> flag to the client.
/// </summary>
public class DoodadFuncPrivateCoffer : DoodadFuncCoffer
{
    public bool IsManikin { get; set; }
    public HashSet<int> AllowedItemCategoryIds { get; } = [];
}
