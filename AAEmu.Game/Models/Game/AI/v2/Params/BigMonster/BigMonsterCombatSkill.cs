using System;
using NLua;

namespace AAEmu.Game.Models.Game.AI.V2.Params.BigMonster
{
    public class BigMonsterCombatSkill
    {
        public uint SkillType { get; set; }
        public float SkillDelay { get; set; }
        public bool StrafeDuringDelay { get; set; }
        public int HealthRangeMin { get; set; }
        public int HealthRangeMax { get; set; }
        
        public void ParseLua(LuaTable table)
        {
            if (table["healthRange"] is LuaTable healthRange)
            {
                HealthRangeMin = Convert.ToInt32(healthRange[1]);
                HealthRangeMax = Convert.ToInt32(healthRange[2]);
            }

            SkillType = Convert.ToUInt32(table["skillType"]);
            SkillDelay = Convert.ToSingle(table["skillDelay"]);
            StrafeDuringDelay = Convert.ToBoolean(table["strafeDuringDelay"]);
        }
    }
}
