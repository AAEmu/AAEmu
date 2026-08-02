using System.Numerics;
using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Models.Spheres;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils.DB;
using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

[GameData]
public class UnitRequirementsGameData : Singleton<UnitRequirementsGameData>, IGameDataLoader
{
    /// <summary>
    /// Id, unit_reqs
    /// </summary>
    private Dictionary<uint, UnitReqs> _unitReqs { get; set; }

    /// <summary>
    /// owner_type, owner_id, unit_reqs
    /// </summary>
    private Dictionary<string, List<UnitReqs>> _unitReqsByOwnerType { get; set; }

    public void Load(SqliteConnection connection)
    {
        _unitReqs = [];
        _unitReqsByOwnerType = [];

        #region Tables

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM unit_reqs";
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
        {
            if (!reader.GetBoolean("enable"))
                continue;

            var t = new UnitReqs
            {
                Id = reader.GetUInt32("id"), OwnerId = reader.GetUInt32("owner_id"), OwnerType = reader.GetString("owner_type"),
                KindType = (UnitReqsKindType)reader.GetUInt32("kind_id"),
                Value1 = reader.GetUInt32("value1"),
                Value2 = reader.GetUInt32("value2"),
                Value3 = reader.GetUInt32("value3"),
                DisplayMessage = reader.GetBoolean("display_msg")
            };

            _unitReqs.TryAdd(t.Id, t);
            if (!_unitReqsByOwnerType.ContainsKey(t.OwnerType))
                _unitReqsByOwnerType.TryAdd(t.OwnerType, []);
            _unitReqsByOwnerType[t.OwnerType].Add(t);
        }
        #endregion
    }

    public void PostLoad()
    {
        // Maybe bind requirement system directly into target templates for optimization?
    }

    private IEnumerable<UnitReqs> GetRequirement(string ownerType, uint ownerId)
    {
        if (!_unitReqsByOwnerType.TryGetValue(ownerType, out var unitReqsMap))
            return [];
        return unitReqsMap.Where(x => x.OwnerId == ownerId);
    }

    public bool CanUseItemBag(ItemBagTemplate itemBag, Character character)
    {
        ArgumentNullException.ThrowIfNull(itemBag);
        if (character == null)
            return false;

        var requirements = GetRequirement(nameof(ItemBag), itemBag.ItemBagId).ToList();
        if (requirements.Count == 0)
            return true;

        return itemBag.OrUnitReqs
            ? requirements.Any(requirement => requirement.Validate(character, character).ResultKey == SkillResultKeys.ok)
            : requirements.All(requirement => requirement.Validate(character, character).ResultKey == SkillResultKeys.ok);
    }

    public List<UnitReqs> GetSkillRequirements(uint skillId)
    {
        return GetRequirement("Skill", skillId).ToList();
    }

    public List<UnitReqs> GetAchievementObjectiveRequirements(uint achievementObjectiveId)
    {
        return GetRequirement("AchievementObjective", achievementObjectiveId).ToList();
    }

    public List<UnitReqs> GetAiEventRequirements(uint aiEvent)
    {
        return GetRequirement("AiEvent", aiEvent).ToList();
    }

    public List<UnitReqs> GetItemArmorRequirements(uint armorId)
    {
        return GetRequirement("ItemArmor", armorId).ToList();
    }

    public List<UnitReqs> GetItemWeaponRequirements(uint weaponId)
    {
        return GetRequirement("ItemWeapon", weaponId).ToList();
    }

    public List<UnitReqs> GetQuestComponentRequirements(uint componentId)
    {
        return GetRequirement("QuestComponent", componentId).ToList();
    }

    public List<UnitReqs> GetSphereRequirements(uint sphereId)
    {
        return GetRequirement("Sphere", sphereId).ToList();
    }

    public TreasureMap GetTreasureMapWithCoordinatesNearbyItem(Character character, double maxRange)
    {
        if (character == null)
            return null;
        if (!character.Inventory.GetAllItemsByTemplate([SlotType.Inventory], Item.TreasureMapWithCoordinates, -1, out var maps, out _))
            return null;
        foreach (var map in maps)
        {
            if (map is TreasureMap treasureMap)
            {
                var dist = character.Transform.World.Position - treasureMap.GetMapPosition(character.Transform.World.Position.Z);
                if (dist.Length() <= maxRange)
                    return treasureMap;
            }
        }
        return null;
    }

    /// <summary>
    /// Checks if a Unit is able to use a Skill
    /// </summary>
    /// <param name="skillTemplate"></param>
    /// <param name="ownerUnit"></param>
    /// <param name="skillCaster"></param>
    /// <returns></returns>
    public UnitReqsValidationResult CanUseSkill(
        SkillTemplate skillTemplate,
        BaseUnit ownerUnit,
        SkillCaster skillCaster,
        SkillCastTarget skillTarget)
    {
        // Buried Treasure check, I can't seem to find any table that adds this requirement
        // Note by ZeromusXYZ:
        // I don't like putting the check here, but it feels like the best options since there does not seem to be
        // any tables that could be used to identify that this skill needs a check
        if (skillTemplate.Id == SkillsEnum.DigUpTreasureChestMarkedOnMap)
        {
            var treasureMap = GetTreasureMapWithCoordinatesNearbyItem(ownerUnit as Character, skillTemplate.MaxRange);
            if (treasureMap == null)
            {
                return new UnitReqsValidationResult(SkillResultKeys.skill_urk_own_item, 0, Item.TreasureMapWithCoordinates);
            }
        }

        // if (skillTemplate == null)
        //     return new UnitReqsValidationResult(SkillResultKeys.skill_invalid_skill, 0, 0);
        var reqs = GetSkillRequirements(skillTemplate.Id);
        if (reqs.Count == 0)
            return new UnitReqsValidationResult(SkillResultKeys.ok, 0, 0); // SkillResult.Success; // No requirements, we're good

        // Used for special handling hack for AreaSphere requirement
        // Example QuestId: 5079 & 5080 - Guerilla Marketing
        var validQuestComponents = new List<uint>();

        // For skill for "item use" for specific quests
        if (skillCaster is SkillItem skillItem && ownerUnit is Character player)
        {
            var actsUsingItem = player.Quests.GetActiveActsWithUseItem(skillItem.ItemTemplateId);
            foreach (var act in actsUsingItem)
            {
                if (!validQuestComponents.Contains(act.Template.ParentComponent.Id))
                    validQuestComponents.Add(act.Template.ParentComponent.Id);
            }
        }
        // TODO: check if there are any other skill types that required to be used in a specific area of multiple quest spheres

        // Unit requirements apply to the target carried by this cast. CurrentTarget is UI state and
        // may point at another unit (or lag behind the packet), so it cannot authorize Target* kinds.
        var target = skillTemplate.TargetType == SkillTargetType.Self
            ? ownerUnit
            : skillTarget is SkillCastUnitTarget or SkillCastDoodadTarget
                ? skillTarget.ObjId > 0
                    ? ownerUnit?.ParentWorld?.GetBaseUnit(skillTarget.ObjId)
                    : ownerUnit
                : ownerUnit;
        var targetItem = ownerUnit is Character itemOwner && skillTarget is SkillCastItemTarget itemTarget
            ? itemOwner.Inventory.GetItemById(itemTarget.Id)
            : null;
        
        var res = !skillTemplate.OrUnitReqs;
        var lastFailedCheckResult = new UnitReqsValidationResult(SkillResultKeys.skill_failure, 0, 0);
        foreach (var unitReq in reqs)
        {
            var reqRes = false;
            if (unitReq.KindType == UnitReqsKindType.AreaSphere && validQuestComponents.Count > 0)
            {
                // Special handling for quests spheres with items
                foreach (var requiredComponentId in validQuestComponents)
                {
                    var foundSphere = SphereGameData.Instance.IsInsideAreaSphere(unitReq.Value1, unitReq.Value2, ownerUnit?.Transform?.World?.Position ?? Vector3.Zero, requiredComponentId);
                    reqRes = foundSphere != null;
                    var lastCheckResult = new UnitReqsValidationResult(reqRes ? SkillResultKeys.ok : SkillResultKeys.skill_urk_area_sphere, 0, unitReq.Value1);
                    if (lastCheckResult.ResultKey != SkillResultKeys.ok)
                        lastFailedCheckResult = lastCheckResult;
                    if (reqRes)
                        break;
                }
            }
            else
            {
                var lastCheckResult = unitReq.Validate(ownerUnit, target, targetItem);
                reqRes = lastCheckResult.ResultKey == SkillResultKeys.ok;
                if (lastCheckResult.ResultKey != SkillResultKeys.ok)
                    lastFailedCheckResult = lastCheckResult;
            }

            if (skillTemplate.OrUnitReqs && reqRes)
            {
                res = true;
                break;
            }

            if (!skillTemplate.OrUnitReqs && !reqRes)
            {
                res = false;
                break;
            }
        }

        return res ? new UnitReqsValidationResult(SkillResultKeys.ok, 0, 0) : lastFailedCheckResult;
    }

    public bool CanTriggerSphere(Spheres sphere, BaseUnit ownerUnit)
    {
        var reqs = GetSphereRequirements(sphere.Id);
        if (reqs.Count == 0)
            return true; // No requirements, we're good
        
        var target = (ownerUnit as Unit)?.CurrentTarget ?? ownerUnit;
        
        var res = !sphere.OrUnitReqs;
        foreach (var unitReq in reqs)
        {
            var validateRes = unitReq.Validate(ownerUnit, target);
            var reqRes = validateRes.ResultKey == SkillResultKeys.ok;

            if (sphere.OrUnitReqs && reqRes)
            {
                res = true;
                break;
            }

            if (!sphere.OrUnitReqs && !reqRes)
            {
                res = false;
                break;
            }
        }
        return res;
    }

    public bool CanComponentRun(QuestComponentTemplate questComponent, BaseUnit ownerUnit)
    {
        var reqs = GetQuestComponentRequirements(questComponent.Id);
        if (reqs.Count == 0)
            return true; // No requirements, we're good
        var target = (ownerUnit as Unit)?.CurrentTarget ?? ownerUnit;
        var res = !questComponent.OrUnitReqs;
        foreach (var unitReq in reqs)
        {
            var validateRes = unitReq.Validate(ownerUnit, target);
            var reqRes = validateRes.ResultKey == SkillResultKeys.ok;

            if (questComponent.OrUnitReqs && reqRes)
            {
                res = true;
                break;
            }

            if (!questComponent.OrUnitReqs && !reqRes)
            {
                res = false;
                break;
            }
        }
        return res;
    }
}
