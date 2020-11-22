using System;
using System.Collections.Generic;
using System.Text;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public enum SkillEffectApplicationMethod
    {
        Target = 0x1,
        Source = 0x2,
        SourceOnce = 0x3,
        SourceToPos = 0x4,
    }
}
