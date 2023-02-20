using System;
using System.Collections.Generic;

using NLua;

namespace AAEmu.Game.Models.Game.AI.V2.Params
{
    public class AiSkillList
    {
        public string PipeName { get; set; }
        public uint PhaseType { get; set; } // Should be enum EX. PHASE_DRAGON_GROUND
        public uint UseType { get; set; } // Should be enum EX. USE_SEQUENCE
        public int Dice { get; set; }
        public int HealthRangeMin { get; set; }
        public int HealthRangeMax { get; set; }
        public int TimeRangeStart { get; set; }
        public int TimeRangeEnd { get; set; }
        public List<AiSkill> Skills { get; set; }
        public bool Restoration { get; set; }
        public bool GoReturn { get; set; }

        public void ParseLua(LuaTable table)
        {
            PipeName = Convert.ToString(table["pipeName"]);
            PhaseType = Convert.ToUInt32(table["phaseType"]);
            UseType = Convert.ToUInt32(table["useType"]);
            Dice = Convert.ToInt32(table["dice"]);

            if (table["healthRange"] is LuaTable healthRange)
            {
                HealthRangeMin = Convert.ToInt32(healthRange[1]);
                HealthRangeMax = Convert.ToInt32(healthRange[2]);
            }

            if (table["timeRange"] is LuaTable timeRange)
            {
                TimeRangeStart = Convert.ToInt32(timeRange[1]);
                TimeRangeEnd = Convert.ToInt32(timeRange[2]);
            }

            if (table["options"] is LuaTable options)
            {
                Restoration = Convert.ToBoolean(options["restoration"]);
                GoReturn = Convert.ToBoolean(options["goReturn"]);
            }

            Skills = new List<AiSkill>();

            if (table["startAiSkill"] is LuaTable startAiSkill)
            {
                var aiSkill = new AiSkill();
                aiSkill.SkillId = Convert.ToUInt32(startAiSkill["skillType"]);
                aiSkill.Delay = Convert.ToSingle(startAiSkill["delay"]);
                aiSkill.Strafe = Convert.ToBoolean(startAiSkill["strafe"]);

                Skills.Add(aiSkill);
            }

            if (table["skillLists"] is LuaTable skillLists)
            {
                foreach (var skillList in skillLists.Values)
                {
                    if (skillList is LuaTable skillListTable)
                    {
                        UseType = Convert.ToUInt32(skillListTable["useType"]);
                        Dice = Convert.ToInt32(skillListTable["dice"]);

                        if (skillListTable["healthRange"] is LuaTable healthRange2)
                        {
                            HealthRangeMin = Convert.ToInt32(healthRange2[1]);
                            HealthRangeMax = Convert.ToInt32(healthRange2[2]);
                        }
                        if (skillListTable["skills"] is LuaTable skills)
                        {
                            foreach (var skill in skills.Values)
                            {
                                if (skill is LuaTable skillTable)
                                {
                                    var aiSkill = new AiSkill();
                                    aiSkill.SkillId = Convert.ToUInt32(skillTable["skillType"]);
                                    aiSkill.Delay = Convert.ToSingle(skillTable["delay"]);
                                    aiSkill.Strafe = Convert.ToBoolean(skillTable["strafe"]);

                                    Skills.Add(aiSkill);
                                }
                            }
                        }
                        else
                        {
                            var aiSkill = new AiSkill();
                            aiSkill.SkillId = Convert.ToUInt32(table["skillType"]);
                            aiSkill.Delay = Convert.ToSingle(table["delay"]);
                            aiSkill.Strafe = Convert.ToBoolean(table["strafe"]);

                            Skills.Add(aiSkill);
                        }
                    }
                }
            }
            else if (table["skills"] is LuaTable skills)
            {
                foreach (var skill in skills.Values)
                {
                    if (skill is LuaTable skillTable)
                    {
                        var aiSkill = new AiSkill();
                        aiSkill.SkillId = Convert.ToUInt32(skillTable["skillType"]);
                        aiSkill.Delay = Convert.ToSingle(skillTable["delay"]);
                        aiSkill.Strafe = Convert.ToBoolean(skillTable["strafe"]);

                        Skills.Add(aiSkill);
                    }
                }
            }
            else
            {
                var aiSkill = new AiSkill();
                aiSkill.SkillId = Convert.ToUInt32(table["skillType"]);
                aiSkill.Delay = Convert.ToSingle(table["delay"]);
                aiSkill.Strafe = Convert.ToBoolean(table["strafe"]);

                Skills.Add(aiSkill);
            }
        }
    }

    public class AiSkill
    {
        public uint SkillId { get; set; }
        public float Delay { get; set; }
        public bool Strafe { get; set; }
    }
}
