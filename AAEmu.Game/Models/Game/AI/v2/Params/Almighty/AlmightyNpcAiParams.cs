using System;
using System.Collections.Generic;

using AAEmu.Game.Models.Game.AI.V2.Params;

using NLua;

namespace AAEmu.Game.Models.Game.AI.v2.Params.Almighty;

public class AlmightyNpcAiParams : AiParams
{
    public List<string> Msgs { get; set; }
    public string IdleAi { get; set; } = "hold_position";
    public bool AlertToAttack { get; set; }
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
        if (aiParams.GetObjectFromPath("data.alertDuration") !=null)
            AlertDuration = (float)aiParams.GetNumber("data.alertDuration");
        if (aiParams.GetObjectFromPath("data.alertSafeTargetRememberTime") !=null)
            AlertSafeTargetRememberTime = (float)aiParams.GetNumber("data.alertSafeTargetRememberTime");
        AlwaysTeleportOnReturn = Convert.ToBoolean(aiParams.GetObjectFromPath("data.alwaysTeleportOnReturn"));
        MaxMakeAGapCount = aiParams.GetInteger("data.maxMakeAGapCount");
        if (aiParams.GetObjectFromPath("data.meleeAttackRange") !=null)
            MeleeAttackRange = (float)aiParams.GetNumber("data.meleeAttackRange");
        if (aiParams.GetObjectFromPath("data.preferedCombatDist") !=null)
            PreferedCombatDist = (float)aiParams.GetNumber("data.preferedCombatDist");
        RestorationOnReturn = Convert.ToBoolean(aiParams.GetObjectFromPath("data.restorationOnReturn"));

        // individually
        AiPhaseChangeType = aiParams.GetInteger("data.aiPhaseChangeType");
        AlertToAttack = Convert.ToBoolean(aiParams.GetObjectFromPath("data.alertToAttack"));
        CanChangeAiUnitAttr = aiParams.GetInteger("data.canChangeAiUnitAttr");
        IdleAi = (string)aiParams.GetObjectFromPath("data.idle_ai") ?? "";

        // aiPhase not seem to be used?
        AiSkillLists = new List<AiSkillList>();
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

        AiPathSkillLists = new List<AiSkillList>();
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

        AiPathDamageSkillLists = new List<AiSkillList>();
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
