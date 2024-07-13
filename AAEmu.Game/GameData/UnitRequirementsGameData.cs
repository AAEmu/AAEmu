﻿using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Models.Spheres;
using AAEmu.Game.Utils.DB;
using Microsoft.Data.Sqlite;
using NLog;

namespace AAEmu.Game.GameData;

[GameData]
public class UnitRequirementsGameData : Singleton<UnitRequirementsGameData>, IGameDataLoader
{
    private Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Id, unit_reqs
    /// </summary>
    private Dictionary<uint, UnitReqs> _unitReqs { get; set; }

    /// <summary>
    /// owner_type, owner_id, unit_reqs
    /// </summary>
    private Dictionary<string, List<UnitReqs>> _unitReqsByOwnerType { get; set; }
    
    public void Load(SqliteConnection connection, SqliteConnection connection2)
    {
        _unitReqs = new();
        _unitReqsByOwnerType = new();

        #region Tables

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM unit_reqs";
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
        {
            var t = new UnitReqs();
            t.Id = reader.GetUInt32("id");
            t.OwnerId = reader.GetUInt32("owner_id");
            t.OwnerType = reader.GetString("owner_type");
            t.KindType = (UnitReqsKindType)reader.GetUInt32("kind_id");
            t.Value1 = reader.GetUInt32("value1");
            t.Value2 = reader.GetUInt32("value2");

            _unitReqs.TryAdd(t.Id, t);
            if (!_unitReqsByOwnerType.ContainsKey(t.OwnerType))
                _unitReqsByOwnerType.TryAdd(t.OwnerType, new List<UnitReqs>());
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
            return null;
        return unitReqsMap.Where(x => x.OwnerId == ownerId);
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

    /// <summary>
    /// Checks if a Unit is able to use a Skill
    /// </summary>
    /// <param name="skillTemplate"></param>
    /// <param name="ownerUnit"></param>
    /// <returns></returns>
    public UnitReqsValidationResult CanUseSkill(SkillTemplate skillTemplate, BaseUnit ownerUnit, SkillCaster skillCaster)
    {
        if (skillTemplate == null)
            return new UnitReqsValidationResult(SkillResultKeys.skill_invalid_skill, 0, 0);
        var reqs = GetSkillRequirements(skillTemplate.Id);
        if (reqs.Count == 0)
            return new UnitReqsValidationResult(SkillResultKeys.ok, 0, 0); // SkillResult.Success; // No requirements, we're good

        // Used for special handling hack for AreaSphere requirement
        // Example QuestId: 5079 & 5080 - Guerilla Marketing
        var validQuestComponents = new List<uint>();
        
        // For skill for "item use" for specific quests
        if ((skillCaster is SkillItem skillItem) && (ownerUnit is Character player))
        {
            var actsUsingItem = player.Quests.GetActiveActsWithUseItem(skillItem.ItemTemplateId);
            foreach (var act in actsUsingItem)
            {
                if (!validQuestComponents.Contains(act.Template.ParentComponent.Id))
                    validQuestComponents.Add(act.Template.ParentComponent.Id);
            }
        }
        // TODO: check if there are any other skill types that required to be used in a specific area of multiple quest spheres
        
        var res = !skillTemplate.OrUnitReqs;
        var lastCheckResult = new UnitReqsValidationResult(SkillResultKeys.ok,0,0);
        foreach (var unitReq in reqs)
        {
            var reqRes = false;
            if ((unitReq.KindType == UnitReqsKindType.AreaSphere) && (validQuestComponents.Count > 0))
            {
                // Special handling for quests spheres with items
                foreach (var requiredComponentId in validQuestComponents)
                {
                    var foundSphere = SphereGameData.Instance.IsInsideAreaSphere(unitReq.Value1, unitReq.Value2, ownerUnit?.Transform?.World?.Position ?? Vector3.Zero, requiredComponentId);
                    reqRes = foundSphere != null;
                    lastCheckResult = new UnitReqsValidationResult(reqRes ? SkillResultKeys.ok : SkillResultKeys.skill_urk_area_sphere, 0, unitReq.Value1);
                    if (reqRes)
                        break;
                }
            }
            else
            {
                lastCheckResult = unitReq.Validate(ownerUnit);
                reqRes = lastCheckResult.ResultKey == SkillResultKeys.ok;                
            }

            if (skillTemplate.OrUnitReqs)
            {
                // If OrUnitReqs is set, stop checking at the first hit
                res = true;
                break;
            }

            res &= reqRes;
        }

        return res ? new UnitReqsValidationResult(SkillResultKeys.ok, 0, 0) : lastCheckResult;
    }
    
    public bool CanTriggerSphere(Spheres sphere, BaseUnit ownerUnit)
    {
        var reqs = GetSphereRequirements(sphere.Id);
        if (reqs.Count == 0)
            return true; // No requirements, we're good
        var res = !sphere.OrUnitReqs;
        foreach (var unitReq in reqs)
        {
            var validateRes = unitReq.Validate(ownerUnit);
            var reqRes = validateRes.ResultKey == SkillResultKeys.ok;

            if (sphere.OrUnitReqs)
            {
                // If OrUnitReqs is set, stop checking at the first hit
                res = true;
                break;
            }

            res &= reqRes;
        }
        return res;
    }
    
    public bool CanComponentRun(QuestComponentTemplate questComponent, BaseUnit ownerUnit)
    {
        var reqs = GetQuestComponentRequirements(questComponent.Id);
        if (reqs.Count == 0)
            return true; // No requirements, we're good
        var res = !questComponent.OrUnitReqs;
        foreach (var unitReq in reqs)
        {
            var validateRes = unitReq.Validate(ownerUnit);
            var reqRes = validateRes.ResultKey == SkillResultKeys.ok;

            if (questComponent.OrUnitReqs)
            {
                // If OrUnitReqs is set, stop checking at the first hit
                res = true;
                break;
            }

            res &= reqRes;
        }
        return res;
    }
}
