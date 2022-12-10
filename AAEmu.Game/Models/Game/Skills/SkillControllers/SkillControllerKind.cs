using System;
using System.Collections.Generic;
using System.Text;

namespace AAEmu.Game.Models.Game.Skills.SkillControllers
{
    enum SkillControllerKind
    {
        None = 0x0,
        Floating = 0x1,
        Leap = 0x2,
        Wandering = 0x3,
        Dash = 0x4,
        Rope = 0x5,
        Anchor = 0x6,
        Rotate = 0x7,
        Flowgraph = 0x8,
    };
}
