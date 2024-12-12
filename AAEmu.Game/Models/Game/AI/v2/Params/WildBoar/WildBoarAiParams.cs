using System;
using System.Collections.Generic;

using AAEmu.Game.Models.Game.AI.V2.Params;

using NLua;

namespace AAEmu.Game.Models.Game.AI.v2.Params.WildBoar;

public class WildBoarAiParams : AiParams
{
    //1047
    // onCombatStartSkill = { 15625 }, 
    // onSpurtSkill = {
    //     { skillType = 14038, healthCondition = 70 },
    // },
    public List<uint> OnCombatStartSkills { get; set; }
    public List<WildBoarAiSpurtSkill> OnSpurtSkills { get; set; }

    public WildBoarAiParams(string aiPramsString)
    {
        Parse(aiPramsString);
    }

    private void Parse(string data)
    {
        using var aiParams = new AiLua();
        aiParams.DoString($"data = {{\n{data}\n}}");

        // general
        if (aiParams.GetObjectFromPath("data.alertDuration") != null)
            AlertDuration = (float)aiParams.GetNumber("data.alertDuration");
        if (aiParams.GetObjectFromPath("data.alertToAttack") != null)
            AlertToAttack = Convert.ToBoolean(aiParams.GetObjectFromPath("data.alertToAttack"));
        if (aiParams.GetObjectFromPath("data.alertSafeTargetRememberTime") != null)
            AlertSafeTargetRememberTime = (float)aiParams.GetNumber("data.alertSafeTargetRememberTime");
        if (aiParams.GetObjectFromPath("data.alwaysTeleportOnReturn") != null)
            AlwaysTeleportOnReturn = Convert.ToBoolean(aiParams.GetObjectFromPath("data.alwaysTeleportOnReturn"));
        if (aiParams.GetObjectFromPath("data.maxMakeAGapCount") != null)
            MaxMakeAGapCount = aiParams.GetInteger("data.maxMakeAGapCount");
        if (aiParams.GetObjectFromPath("data.meleeAttackRange") != null)
            MeleeAttackRange = (float)aiParams.GetNumber("data.meleeAttackRange");
        if (aiParams.GetObjectFromPath("data.preferedCombatDist") != null)
            PreferedCombatDist = (float)aiParams.GetNumber("data.preferedCombatDist");
        if (aiParams.GetObjectFromPath("data.restorationOnReturn") != null)
            RestorationOnReturn = Convert.ToBoolean(aiParams.GetObjectFromPath("data.restorationOnReturn"));

        // individually
        OnCombatStartSkills = [];
        if (aiParams.GetTable("data.onCombatStartSkill") is LuaTable combatStartSkillsTable)
        {
            foreach (var value in combatStartSkillsTable.Values)
            {
                OnCombatStartSkills.Add(Convert.ToUInt32(value));
            }
        }
        // данные об OnSpurtSkills можно парсить так
        //if (aiParams.GetTable("data.onSpurtSkill") is LuaTable spurtSkillTable)
        //{
        //    OnSpurtSkills = new List<WildBoarAiSpurtSkill>();
        //    foreach (var value in spurtSkillTable.Values)
        //    {
        //        if (value is LuaTable spurtSkill)
        //        {
        //            OnSpurtSkills.Add(new WildBoarAiSpurtSkill()
        //            {
        //                SkillType = Convert.ToUInt32(spurtSkill["skillType"]),
        //                HealthCondition = Convert.ToUInt32(spurtSkill["healthCondition"])
        //            });
        //        }
        //    }
        //}
        // или данные об OnSpurtSkills можно парсить так
        OnSpurtSkills = [];
        if (aiParams.GetTable("data.onSpurtSkill") is LuaTable spurtSkillTable)
        {
            foreach (var skillList in spurtSkillTable.Values)
            {
                if (skillList is not LuaTable skillListTable)
                    continue;

                var skill = new WildBoarAiSpurtSkill();
                skill.ParseLua(skillListTable);

                OnSpurtSkills.Add(skill);
            }
        }
    }
}
