using System;
using System.Collections.Generic;
using System.Text;
using AAEmu.Game.Models.Game.AI.v2.Params;
using NLua;

namespace AAEmu.Game.Models.Game.AI.V2.Params
{
    public class AlmightyNpcAiParams : AiParams
    {
        public List<string> Msgs { get; set; }
        public string IdleAi { get; set; } = "hold_position";

        public float AlertDuration { get; set; } = 3.0f;
        public float AlertSafeTargetRememberTime { get; set; } = 5.0f;
        public int CanChangeAiUnitAttr { get; set; } = 0;

        public int MeleeAttackRange { get; set; }
        public int PreferedCombatDist { get; set; } = 0;
        public int MaxMakeAGapCount { get; set; } = 0;

        public int AiPhaseChangeType { get; set; } = 0;//Should be an enum

        public List<int> AiPhase { get; set; }//this might be enum 
        public List<AiSkillList> AiSkillLists { get; set; }
        public List<AiSkillList> AiPathSkillLists { get; set; }

        public List<AiSkillList> AiPathDamageSkillLists { get; set; }

        public AlmightyNpcAiParams(string aiParamsString)
        {
            Parse(aiParamsString);
        }

        private void Parse(string data)
        {
            using (var aiParams = new AiLua())
            {
                aiParams.DoString($"data = {{\n{data}\n}}");
                IdleAi = (string)aiParams.GetObjectFromPath("data.idle_ai") ?? "";
                AlertDuration = Convert.ToSingle(aiParams.GetObjectFromPath("data.alertDuration"));
                AlertSafeTargetRememberTime = Convert.ToSingle(aiParams.GetObjectFromPath("data.alertSafeTargetRememberTime"));
                CanChangeAiUnitAttr = aiParams.GetInteger("data.canChangeAiUnitAttr");
                MeleeAttackRange = aiParams.GetInteger("data.meleeAttackRange");
                PreferedCombatDist = aiParams.GetInteger("data.preferedCombatDist");
                MaxMakeAGapCount = aiParams.GetInteger("data.maxMakeAGapCount");
                AiPhaseChangeType = aiParams.GetInteger("data.aiPhaseChangeType");

                //aiPhase not seem to be used?
                AiSkillLists = new List<AiSkillList>();
                if(aiParams.GetTable("data.aiSkillLists") is LuaTable table)
                {
                    foreach(var skillList in table.Values)
                    {
                        if(skillList is LuaTable skillListTable)
                        {
                            var aiSkillList = new AiSkillList();
                            aiSkillList.ParseLua(skillListTable);

                            AiSkillLists.Add(aiSkillList);
                        }
                    }
                }

                if (aiParams.GetTable("data.aiPathSkillLists") is LuaTable pathSkills)
                {
                    AiPathSkillLists = new List<AiSkillList>();
                    foreach (var skillList in pathSkills.Values)
                    {
                        if (skillList is LuaTable skillListTable)
                        {
                            var aiSkillList = new AiSkillList();
                            aiSkillList.ParseLua(skillListTable);

                            AiPathSkillLists.Add(aiSkillList);
                        }
                    }
                }

                if (aiParams.GetTable("data.aiPathDamageSkillLists") is LuaTable pathDamageSkills)
                {
                    AiPathDamageSkillLists = new List<AiSkillList>();
                    foreach (var skillList in pathDamageSkills.Values)
                    {
                        if (skillList is LuaTable skillListTable)
                        {
                            var aiSkillList = new AiSkillList();
                            aiSkillList.ParseLua(skillListTable);

                            AiPathDamageSkillLists.Add(aiSkillList);
                        }
                    }
                }
            }
        }
    }
}
