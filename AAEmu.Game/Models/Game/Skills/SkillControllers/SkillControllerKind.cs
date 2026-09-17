namespace AAEmu.Game.Models.Game.Skills.SkillControllers;

internal enum SkillControllerKind
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
    // 10.0.2.13: enum_skill_controller_kinds has 11 rows and stops at 11; there is no 0xA.
    RopeReady = 0x9,
    Crawl = 0xB,
};
