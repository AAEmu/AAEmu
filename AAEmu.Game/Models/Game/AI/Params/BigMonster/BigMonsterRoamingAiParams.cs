using System;
using System.Collections.Generic;
using NLua;

namespace AAEmu.Game.Models.Game.AI.Params.BigMonster
{
    public class BigMonsterRoamingAiParams : AiParamsOld
    {
        public override AiParamType Type => AiParamType.BigMonsterRoaming;

        public float AlertDuration { get; set; } = 3.0f;
        public float AlertSafeTargetRememberTime { get; set; } = 5.0f;
        public bool AlertToAttack { get; set; } = true;
        public List<BigMonsterCombatSkill> CombatSkills { get; set; }

        public override void Parse(string data)
        {
            using (var aiParams = new AiLua())
            {
                aiParams.DoString($"data = {{\n{data}\n}}");
                
                AlertDuration = aiParams.GetInteger("data.alertDuration");
                AlertSafeTargetRememberTime = aiParams.GetInteger("data.alertSafeTargetRememberTime");
                AlertToAttack = Convert.ToBoolean(aiParams.GetString("data.alertToAttack"));
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
