using System;
using System.Collections.Generic;
using AAEmu.Game.Models.Game.AI.V2.Params;
using NLua;

namespace AAEmu.Game.Models.Game.AI.v2.Params.WildBoar
{
    public class WildBoarAiParams : AiParams
    {
        public float AlertDuration { get; set; } = 3.0f;
        public float AlertSafeTargetRememberTime { get; set; } = 5.0f;
        
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
            using (var aiParams = new AiLua())
            {
                aiParams.DoString($"data = {{\n{data}\n}}");

                if (aiParams.GetObjectFromPath("data.alertDuration") != null)
                    AlertDuration = aiParams.GetInteger("data.alertDuration");
                if (aiParams.GetObjectFromPath("data.alertSafeTargetRememberTime") != null)
                    AlertSafeTargetRememberTime = aiParams.GetInteger("data.alertSafeTargetRememberTime");

                if (aiParams.GetTable("data.combatSkills") is LuaTable table)
                {
                    OnCombatStartSkills = new List<uint>();
                    if (table["onCombatStartSkill"] is LuaTable combatStartSkills)
                    {
                        foreach (var value in combatStartSkills.Values)
                        {
                            OnCombatStartSkills.Add(Convert.ToUInt32(value));
                        }
                    }

                    OnSpurtSkills = new List<WildBoarAiSpurtSkill>();
                    if (table["onSpurtSkill"] is LuaTable onSpurtSkills)
                    {
                        foreach (var value in onSpurtSkills.Values)
                        {
                            if (value is LuaTable spurtSkill)
                            {
                                OnSpurtSkills.Add(new WildBoarAiSpurtSkill()
                                {
                                    SkillType = Convert.ToUInt32(spurtSkill["skillType"]),
                                    HealthCondition = Convert.ToUInt32(spurtSkill["healthCondition"])
                                });
                            }
                        }
                    }
                }
            }
        }
    }
}
