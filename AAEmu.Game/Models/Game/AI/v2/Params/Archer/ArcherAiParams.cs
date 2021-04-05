using System;
using System.Collections.Generic;
using AAEmu.Game.Models.Game.AI.V2.Params;
using AAEmu.Game.Models.Game.AI.V2.Params.Archer;
using NLua;

namespace AAEmu.Game.Models.Game.AI.v2.Params.Archer
{
    public class ArcherAiParams : AiParams
    {
        public float AlertDuration { get; set; } = 3.0f;
        public float AlertSafeTargetRememberTime { get; set; } = 5.0f;
        //public int MeleeAttackRange { get; set; } // This is found in the entity?
        public List<ArcherCombatSkill> CombatSkills { get; set; }
        //public int PreferedCombastDist { get; set; } // Also found in entity
        public int MaxMakeAGapeCount { get; set; } = 3;

        public ArcherAiParams(string aiPramsString)
        {
            Parse(aiPramsString);
        }

        private void Parse(string data)
        {
            using (var aiParams = new AiLua())
            {
                aiParams.DoString($"data = {{\n{data}\n}}");

                if (aiParams.GetObjectFromPath("data.alertDuration") != null)
                    AlertDuration = aiParams.GetInteger("data.alertDuration");
                if (aiParams.GetObjectFromPath("data.alertSafeTargetRememberTime") != null)
                    AlertSafeTargetRememberTime = aiParams.GetInteger("data.alertSafeTargetRememberTime");
                CombatSkills = new List<ArcherCombatSkill>();
                if (aiParams.GetTable("data.combatSkills") is LuaTable table)
                {
                    var combatSkill = new ArcherCombatSkill();
                    combatSkill.ParseLua(table);
                    CombatSkills.Add(combatSkill);
                }
                if (aiParams.GetObjectFromPath("data.maxMakeAGapCount") != null)
                    MaxMakeAGapeCount = aiParams.GetInteger("data.maxMakeAGapCount");
            }
        }
    }
}
