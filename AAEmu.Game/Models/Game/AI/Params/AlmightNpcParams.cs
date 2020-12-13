using System;
using System.Collections.Generic;
using System.Text;

namespace AAEmu.Game.Models.Game.AI.Params
{
    class AlmightNpcParams
    {
        public List<string> Msgs { get; set; }
        public string IdleAi { get; set; } = "hold_position";

        public float AlertDuration { get; set; } = 3.0f;
        public float AlertSafeTargetRememberTime { get; set; } = 5.0f;
        public int CanChangeAiUnitAttr { get; set; } = 0;

        public int MeleeAttackRange { get; set; }
        public int PreferedCombatDist { get; set; } = 0;
        public int MaxMakeAGapCount { get; set; } = 0;

        public int AiPhaseChangeType { get; set; } = 0;//Should be an enum

        public List<int> AiPhase { get; set; }//this might be enum 
        public AiSkillList AiSkillList { get; set; }
        //TODO AiPathSkillLists
        //TODO AiPathDamageSkillLists
    }
}
