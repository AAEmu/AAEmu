namespace AAEmu.Game.Models.Game.World;

public enum AreaShapeType
{
    Sphere = 1,
    Cuboid = 2,
    /// <summary>
    /// enum_aoe_shape_kinds id 3. A forward corridor from the origin towards the anchor named by
    /// the shape's area_target_kind_id. 58 plot events use it, among them Crashing Wave's "라인"
    /// nodes (plot 3523, shapes 13617 and 23158) — the only paths to that plot's damage effect.
    /// </summary>
    Line = 3
}
