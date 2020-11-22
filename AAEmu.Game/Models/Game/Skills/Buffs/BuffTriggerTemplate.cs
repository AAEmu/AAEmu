using System;
using System.Collections.Generic;
using System.Text;
using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills.Buffs
{
    public class BuffTriggerTemplate
    {
        public uint Id { get; set; }
        public BuffEventTriggerKind Kind{ get; set; }
        public bool EffectOnSource { get; set; }
        public EffectTemplate Effect { get; set; }
        public bool UseDamageAmount { get; set; }
        public bool UseOriginalSource { get; set; }
        public uint TargetBuffTagId { get; set; }
        public uint TargetNoBuffTagId { get; set; }
        public bool Synergy { get; set; }
    }
}
