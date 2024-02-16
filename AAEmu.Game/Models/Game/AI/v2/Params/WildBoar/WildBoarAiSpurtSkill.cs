using System;

using NLua;

namespace AAEmu.Game.Models.Game.AI.v2.Params.WildBoar;

public class WildBoarAiSpurtSkill
{
    public uint SkillType { get; set; }
    public uint HealthCondition { get; set; }

    public void ParseLua(LuaTable table)
    {
        SkillType = Convert.ToUInt32(table["skillType"]);
        HealthCondition = Convert.ToUInt32(table["healthCondition"]);
    }
}
