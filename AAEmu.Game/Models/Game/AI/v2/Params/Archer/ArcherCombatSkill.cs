using System;
using System.Collections.Generic;
using NLua;

namespace AAEmu.Game.Models.Game.AI.V2.Params.Archer
{
    public class ArcherCombatSkill
    {
        public List<uint> Melee { get; set; }
        public List<uint> MakeAGap { get; set; }
        public List<uint> RangedDef { get; set; }
        public List<uint> RangedStrong { get; set; }
        
        public void ParseLua(LuaTable table)
        {
            Melee = new List<uint>();
            if (table["melee"] is LuaTable meleeSkills)
            {
                Melee.AddRange(meleeSkills.Values as IEnumerable<uint>);
            }

            MakeAGap = new List<uint>();
            if (table["makeAGap"] is LuaTable makeAGapSkills)
            {
                Melee.AddRange(makeAGapSkills.Values as IEnumerable<uint>);
            }

            RangedDef = new List<uint>();
            if (table["rangedDef"] is LuaTable rangedDefSkills)
            {
                Melee.AddRange(rangedDefSkills.Values as IEnumerable<uint>);
            }

            RangedStrong = new List<uint>();
            if (table["melee"] is LuaTable rangedStrongSkills)
            {
                Melee.AddRange(rangedStrongSkills.Values as IEnumerable<uint>);
            }
        }
    }
}
