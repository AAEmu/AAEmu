namespace AAEmu.Game.Models.Game.World;

/// <summary>
/// enum_plot_area_target_kinds. Names the unit an area shape is AIMED at. Only line shapes need it:
/// every aoe_shapes row with kind_id 3 carries a non-zero value, while spheres almost always carry 0.
/// </summary>
public enum AreaTargetKindType
{
    None = 0,
    OriginalSource = 1,
    OriginalTarget = 2,
    PreviousSource = 3,
    PreviousTarget = 4,
    CurrentPosition = 5
}
