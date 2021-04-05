using System;
using System.Collections.Generic;
using AAEmu.Game.Models.Game.AI.V2.Params;
using AAEmu.Game.Models.Game.AI.V2.Params.BigMonster;
using NLua;

namespace AAEmu.Game.Models.Game.AI.v2.Params.BigMonster
{
    public class BigMonsterAiParams : AiParams
    {
        public float AlertDuration { get; set; } = 3.0f;
        public float AlertSafeTargetRememberTime { get; set; } = 5.0f;
        public bool AlertToAttack { get; set; } = true;
        public List<BigMonsterCombatSkill> CombatSkills { get; set; }

        public BigMonsterAiParams(string aiPramsString)
        {
            Parse(aiPramsString);
        }

        private void Parse(string data)
        {
            using (var aiParams = new AiLua())
            {
                aiParams.DoString($"data = {{\n{data}\n}}");
                
                if (aiParams.GetObjectFromPath("data.alertDuration") != null)
                    AlertDuration = Convert.ToSingle(aiParams.GetObjectFromPath("data.alertDuration"));
                if (aiParams.GetObjectFromPath("data.alertSafeTargetRememberTime") != null)
                    AlertSafeTargetRememberTime = Convert.ToSingle(aiParams.GetObjectFromPath("data.alertSafeTargetRememberTime"));
                if (aiParams.GetObjectFromPath("data.alertToAttack") != null)
                    AlertToAttack = Convert.ToBoolean(aiParams.GetObjectFromPath("data.alertToAttack"));

                CombatSkills = new List<BigMonsterCombatSkill>();
                if (aiParams.GetTable("data.combatSkills") is LuaTable table)
                {
                    foreach (var skillList in table.Values)
                    {
                        if (skillList is LuaTable skillListTable)
                        {
                            var combatSkill = new BigMonsterCombatSkill();
                            combatSkill.ParseLua(skillListTable);
                            CombatSkills.Add(combatSkill);
                        }
                    }
                }
            }
        }
    }
}
