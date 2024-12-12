using System;
using System.Collections.Generic;

using AAEmu.Game.Models.Game.AI.V2.Params;

using NLua;

namespace AAEmu.Game.Models.Game.AI.v2.Params.Almighty;

public class AlmightyNpcAiParams : AiParams
{
    public List<string> Msgs { get; set; }
    public string IdleAi { get; set; } = "hold_position";
    public int CanChangeAiUnitAttr { get; set; }
    public int AiPhaseChangeType { get; set; } // Should be an enum
    public List<int> AiPhase { get; set; } // this might be enum 
    public List<AiSkillList> AiSkillLists { get; set; }
    public List<AiSkillList> AiPathSkillLists { get; set; }
    public List<AiSkillList> AiPathDamageSkillLists { get; set; }

    public AlmightyNpcAiParams(string aiParamsString)
    {
        Parse(aiParamsString);
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
        AiPhaseChangeType = aiParams.GetInteger("data.aiPhaseChangeType");
        CanChangeAiUnitAttr = aiParams.GetInteger("data.canChangeAiUnitAttr");
        IdleAi = (string)aiParams.GetObjectFromPath("data.idle_ai") ?? "";

        // aiPhase not seem to be used?
        AiSkillLists = [];
        if (aiParams.GetTable("data.aiSkillLists") is LuaTable table)
        {
            foreach (var skillList in table.Values)
            {
                if (skillList is not LuaTable skillListTable)
                    continue;

                var aiSkillList = new AiSkillList();
                aiSkillList.ParseLua(skillListTable);

                AiSkillLists.Add(aiSkillList);
            }
        }

        AiPathSkillLists = [];
        if (aiParams.GetTable("data.aiPathSkillLists") is LuaTable pathSkills)
        {
            foreach (var skillList in pathSkills.Values)
            {
                if (skillList is not LuaTable skillListTable)
                    continue;

                var aiSkillList = new AiSkillList();
                aiSkillList.ParseLua(skillListTable);

                AiPathSkillLists.Add(aiSkillList);
            }
        }

        AiPathDamageSkillLists = [];
        if (aiParams.GetTable("data.aiPathDamageSkillLists") is LuaTable pathDamageSkills)
        {
            foreach (var skillList in pathDamageSkills.Values)
            {
                if (skillList is not LuaTable skillListTable)
                    continue;

                var aiSkillList = new AiSkillList();
                aiSkillList.ParseLua(skillListTable);

                AiPathDamageSkillLists.Add(aiSkillList);
            }
        }
    }
}
