using System;
using System.Collections.Generic;

using NLua;

namespace AAEmu.Game.Models.Game.AI.v2.Params.Almighty;

public class SkillList
{
    public uint UseType { get; set; } // Should be enum EX. USE_SEQUENCE
    public int Dice { get; set; }
    public int HealthRangeMin { get; set; }
    public int HealthRangeMax { get; set; }
    public float TimeRangeStart { get; set; }
    public float TimeRangeEnd { get; set; }

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
            TimeRangeStart = Convert.ToSingle(timeRange[1]);
            TimeRangeEnd = Convert.ToSingle(timeRange[2]);
        }

        Skills = [];
        if (table["skills"] is LuaTable skills)
        {
            foreach (var skill in skills.Values)
            {
                if (skill is not LuaTable skillTable)
                    continue;

                var aiSkill = new AiSkill();
                aiSkill.SkillId = Convert.ToUInt32(skillTable["skillType"]);
                aiSkill.Delay = Convert.ToSingle(skillTable["delay"]);
                aiSkill.Strafe = Convert.ToBoolean(skillTable["strafe"]);

                Skills.Add(aiSkill);
            }
        }
    }
}
