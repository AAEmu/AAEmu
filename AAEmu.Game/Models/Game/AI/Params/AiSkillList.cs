using System;
using System.Collections.Generic;
using System.Text;

namespace AAEmu.Game.Models.Game.AI.Params
{
    public class AiSkillList
    {
        public uint UseType { get; set; }//Should be enum EX. USE_SEQUENCE
        public int Dice { get; set; }
        public (int Min, int Max) HealthRange { get; set; }
        public (int Start, int End) TimeRange { get; set; }
        public List<AiSkill> Skills { get; set; }
    }
    public class AiSkill
    {
        public uint SkillId { get; set; }
        public int Delay { get; set; }
        public bool Strage { get; set; }
    }
}
