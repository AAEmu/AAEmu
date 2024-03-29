using System;

using AAEmu.Game.Models.Game.AI.V2.Params;
using AAEmu.Game.Models.Game.AI.V2.Params.Flytrap;

using NLua;

namespace AAEmu.Game.Models.Game.AI.v2.Params.Flytrap;

public class FlytrapAiParams : AiParams
{
    public float AttackEndDistance { get; set; } = 50; // This is found in the entity?
    public FlytrapCombatSkill CombatSkills { get; set; }

    public FlytrapAiParams(string aiPramsString)
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
            if (aiParams.GetObjectFromPath("data.attackEndDistance") != null)
                AttackEndDistance = (float)aiParams.GetNumber("data.attackEndDistance");

            CombatSkills = new FlytrapCombatSkill();
            if (aiParams.GetTable("data.combatSkills") is LuaTable table)
            {
                CombatSkills.ParseLua(table);
            }
        }
}