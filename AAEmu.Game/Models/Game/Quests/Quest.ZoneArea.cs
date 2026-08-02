namespace AAEmu.Game.Models.Game.Quests;

public partial class Quest
{
    /// <summary>
    /// Zone ZWEnterArea — areaId is Cry groupId (not spheres.id).
    /// CharacterQuests reconciles quest_area_sphere.g (stype→spheres.id) for group 16 family.
    /// </summary>
    public void OnZoneAreaEnter(uint areaId) => _ = areaId;

    /// <summary>Zone ZWLeaveArea.</summary>
    public void OnZoneAreaLeave(uint areaId) => _ = areaId;
}
