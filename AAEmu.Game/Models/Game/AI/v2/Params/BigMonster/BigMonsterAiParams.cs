﻿using System;
using System.Collections.Generic;

using AAEmu.Game.Models.Game.AI.V2.Params;
using AAEmu.Game.Models.Game.AI.V2.Params.BigMonster;

using NLua;

namespace AAEmu.Game.Models.Game.AI.v2.Params.BigMonster;

public class BigMonsterAiParams : AiParams
{
    public bool AlertToAttack { get; set; } = true;
    public List<BigMonsterCombatSkill> CombatSkills { get; set; }

    public BigMonsterAiParams(string aiPramsString)
    {
        Parse(aiPramsString);
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
        AlertToAttack = Convert.ToBoolean(aiParams.GetObjectFromPath("data.alertToAttack"));

        CombatSkills = new List<BigMonsterCombatSkill>();
        if (aiParams.GetTable("data.combatSkills") is LuaTable table)
        {
            foreach (var skillList in table.Values)
            {
                if (skillList is not LuaTable skillListTable)
                    continue;

                var combatSkill = new BigMonsterCombatSkill();
                combatSkill.ParseLua(skillListTable);
                CombatSkills.Add(combatSkill);
            }
        }
    }
}
