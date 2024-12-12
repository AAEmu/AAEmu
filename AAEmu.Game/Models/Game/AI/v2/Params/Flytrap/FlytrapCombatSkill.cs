using System;
using System.Collections.Generic;

using NLua;

namespace AAEmu.Game.Models.Game.AI.V2.Params.Flytrap;

public class FlytrapCombatSkill
{
    public List<uint> Melee { get; set; }
    public List<uint> Ranged { get; set; }

    public void ParseLua(LuaTable table)
    {
        Melee = [];
        if (table["melee"] is LuaTable meleeSkills)
        {
            foreach (var value in meleeSkills.Values)
            {
                Melee.Add(Convert.ToUInt32(value));
            }
        }

        Ranged = [];
        if (table["ranged"] is LuaTable rangedSkills)
        {
            foreach (var value in rangedSkills.Values)
            {
                Ranged.Add(Convert.ToUInt32(value));
            }
        }
    }
}
