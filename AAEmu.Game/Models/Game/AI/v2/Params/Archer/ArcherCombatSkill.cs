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
                foreach(var value in meleeSkills.Values)
                {
                    Melee.Add(Convert.ToUInt32(value));
                }
            }

            MakeAGap = new List<uint>();
            if (table["makeAGap"] is LuaTable makeAGapSkills)
            {
                foreach (var value in makeAGapSkills.Values)
                {
                    MakeAGap.Add(Convert.ToUInt32(value));
                }
            }

            RangedDef = new List<uint>();
            if (table["rangedDef"] is LuaTable rangedDefSkills)
            {
                foreach (var value in rangedDefSkills.Values)
                {
                    RangedDef.Add(Convert.ToUInt32(value));
                }
            }

            RangedStrong = new List<uint>();
            if (table["rangedStrong"] is LuaTable rangedStrongSkills)
            {
                foreach (var value in rangedStrongSkills.Values)
                {
                    RangedStrong.Add(Convert.ToUInt32(value));
                }
            }
        }
    }
}
