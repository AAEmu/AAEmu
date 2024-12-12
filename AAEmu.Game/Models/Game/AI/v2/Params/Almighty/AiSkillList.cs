using System;
using System.Collections.Generic;

using NLua;

namespace AAEmu.Game.Models.Game.AI.v2.Params.Almighty;

public class AiSkillList
{
    public string PipeName { get; set; }
    public uint PhaseType { get; set; } // Should be enum EX. PHASE_DRAGON_GROUND
    public uint UseType { get; set; } // Should be enum EX. USE_SEQUENCE
    public int Dice { get; set; }
    public int HealthRangeMin { get; set; }
    public int HealthRangeMax { get; set; }
    public float TimeRangeStart { get; set; }
    public float TimeRangeEnd { get; set; }
    public bool Restoration { get; set; }
    public bool GoReturn { get; set; }
    public List<AiSkill> StartAiSkills { get; set; }
    public List<SkillList> SkillLists { get; set; }

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
            TimeRangeStart = Convert.ToSingle(timeRange[1]);
            TimeRangeEnd = Convert.ToSingle(timeRange[2]);
        }

        if (table["options"] is LuaTable options)
        {
            Restoration = Convert.ToBoolean(options["restoration"]);
            GoReturn = Convert.ToBoolean(options["goReturn"]);
        }

        StartAiSkills = [];
        if (table["startAiSkill"] is LuaTable startAiSkill)
        {
            var aiSkill = new AiSkill();
            aiSkill.SkillId = Convert.ToUInt32(startAiSkill["skillType"]);
            aiSkill.Delay = Convert.ToSingle(startAiSkill["delay"]);
            aiSkill.Strafe = Convert.ToBoolean(startAiSkill["strafe"]);

            StartAiSkills.Add(aiSkill);
        }

        SkillLists = [];
        if (table["skillLists"] is LuaTable skillListsTable)
        {
            foreach (var skillListTable in skillListsTable.Values)
            {
                if (skillListTable is not LuaTable skills)
                    continue;

                var skillList = new SkillList();
                skillList.ParseLua(skills);

                SkillLists.Add(skillList);
            }
        }
        else if (table["skills"] is LuaTable skills)
        {
            var skillList = new SkillList();
            skillList.Skills = [];
            foreach (var skill in skills.Values)
            {
                if (skill is not LuaTable skillTable)
                    continue;

                var aiSkill = new AiSkill();
                aiSkill.SkillId = Convert.ToUInt32(skillTable["skillType"]);
                aiSkill.Delay = Convert.ToSingle(skillTable["delay"]);
                aiSkill.Strafe = Convert.ToBoolean(skillTable["strafe"]);

                skillList.Skills.Add(aiSkill);
            }
            SkillLists.Add(skillList);
        }
        else
        {
            var aiSkill = new AiSkill();
            aiSkill.SkillId = Convert.ToUInt32(table["skillType"]);
            aiSkill.Delay = Convert.ToSingle(table["delay"]);
            aiSkill.Strafe = Convert.ToBoolean(table["strafe"]);

            var skillList = new SkillList();
            skillList.Skills = [aiSkill];
            SkillLists.Add(skillList);
        }
    }
}
