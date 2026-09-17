namespace AAEmu.Game.Models.Game.Skills.Static;

public enum SkillAttribute
{
    ManaCost = 0,
    Cooldown = 1,
    Range = 2,
    AreaRadius = 3,
    CastTime = 4,
    IncomingDamageMul = 5,
    Damage = 10,
    ChannelingTime = 11,
    Heal = 12,
    SkillLevel = 13,
    HighAbilityResourceCost = 14,

    // enum_skill_attribute 15-20. 15 is consumed (the GCD length); 16/17/20 are loaded and named here so a
    // row is not silently an unknown id, but nothing reads them yet.
    GlobalCooldown = 15,
    TargetAngle = 16,
    MinRange = 17,
    MeleeDamageMulAntiNpc = 18,
    RangedDamageMulAntiNpc = 19,
    SpellDamageMulAntiNpc = 20
}
