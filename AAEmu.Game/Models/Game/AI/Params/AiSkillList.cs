using System;
using System.Collections.Generic;
using System.Text;
using NLua;

namespace AAEmu.Game.Models.Game.AI.Params
{
    public class AiSkillList
    {
        public uint UseType { get; set; }//Should be enum EX. USE_SEQUENCE
        public int Dice { get; set; }
        public int HealthRangeMin { get; set; }
        public int HealthRangeMax { get; set; }
        public int TimeRangeStart { get; set; }
        public int TimeRangeEnd { get; set; }
        public List<AiSkill> Skills { get; set; }

        public void ParseLua(LuaTable table)
        {
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

            Skills = new List<AiSkill>();
            if (table["skills"] is LuaTable skills)
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
        }
    }
    public class AiSkill
    {
        public uint SkillId { get; set; }
        public float Delay { get; set; }
        public bool Strafe { get; set; }
    }
}
