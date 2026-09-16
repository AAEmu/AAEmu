namespace AAEmu.Game.Models.Game.Skills.Buffs;

public enum BuffRemoveOn
{
    SourceDead,
    UseSkill,
    Move,
    Death,
    Exempt,
    Land,
    Interaction,
    Unmount,
    Mount,
    Unbond,
    StartSkill,
    AttackSpellDot,
    AttackEtcDot,
    AttackBuffTrigger,
    AttackEtc,
    AttackedSpellDot,
    AttackedEtcDot,
    AttackedBuffTrigger,
    AttackedEtc,
    DamageSpellDot,
    DamageEtcDot,
    DamageBuffTrigger,
    DamageEtc,
    DamagedSpellDot,
    DamagedEtcDot,
    DamagedBuffTrigger,
    DamagedEtc,
    AutoAttack,
    /// <summary>
    /// <c>buffs.remove_by_summoned</c> (110 buffs: the 감정 표현_* poses, 자세 잡기, 예도, 맹세, 위엄,
    /// 열정의 춤). Not one of the client's 28 — the column has no member in the shipped grid — so it is
    /// appended here rather than inserted, and only <c>TriggerRemoveOn</c> reads it.
    /// </summary>
    Summoned,
    /// <summary>
    /// <c>buffs.remove_on_change_equipments</c>, a bitmask over <c>enum_equip_slot</c> ids carried in
    /// <c>TriggerRemoveOn</c>'s value. Also appended, for the same reason.
    /// </summary>
    ChangeEquipments
}
