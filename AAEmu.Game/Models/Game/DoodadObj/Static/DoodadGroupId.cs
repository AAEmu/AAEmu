namespace AAEmu.Game.Models.Game.DoodadObj.Static;

/// <summary>
/// Values from the content database's <c>doodad_groups.id</c> column that grant vocation badges
/// when their doodads are successfully used.
/// </summary>
public enum DoodadGroupId : uint
{
    Deforestation = 2,
    Picking = 3,
    Mining = 4,
    Livestock = 5,
    Agriculture = 12,
    Excavation = 39,
    MarineAgriculture = 40,
    SportFishing = 65,
}
