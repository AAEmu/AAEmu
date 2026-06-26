namespace AAEmu.Game.Models.Game.Skills.Static;

public enum BuffAttribute
{
    ReflectionTargetRatio = 1,
    Chance = 2,
    Damage = 3,
    Duration = 4,
    InDuration = 5,
    AuraRadius = 6,
    GlidingMoveSpeedSlow = 7,
    GlidingMoveSpeedNormal = 8,
    GlidingMoveSpeedFast = 9,
    // 10.0.2.13: enum_buff_attributes adds 10=heal, 11=cooldown_skill
    Heal = 10,
    CooldownSkill = 11
}
