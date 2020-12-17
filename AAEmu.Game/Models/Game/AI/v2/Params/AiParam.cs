using System.Collections.Generic;

namespace AAEmu.Game.Models.Game.AI.v2.Params
{
    // TODO : Load this!!
    public class AiParam
    {
        // TODO: Msgs
        public string IdleAi { get; set; }

        public float AlertDuration { get; set; } = 3.0f;
        public float AlertSafeTargetRememberTime { get; set; } = 5.0f;
        
        public bool CanChangeAiUnitAttr { get; set; }

        public float MeleeAttackRange { get; set; } = 3.0f;
        public float PreferredCombatDist { get; set; }
        public int MaxMakeAGapCount { get; set; } = 3;
        
        // I dislike this already
        public List<AiSkill> BigMonsterCombatSkills { get; set; }
    }
}
