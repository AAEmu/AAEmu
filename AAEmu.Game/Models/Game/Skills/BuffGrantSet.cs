namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// One <c>buff_swap_skills</c> row: while <see cref="BuffId"/> is active, the action-bar entry
/// <see cref="OriginSkillId"/> is replaced by <see cref="NewSkillId"/>.
/// </summary>
/// <param name="Id"><c>buff_swap_skills.id</c>. Used as the stable tie-break between two swaps that
/// claim the same origin with the same priority.</param>
/// <param name="Priority"><c>buff_swap_skills.priority</c>. The higher value wins; the shipped data uses
/// 0 for almost every row and 1 for the rows that must beat the rest (e.g. 망치 태세 18383 over the other
/// 폭탄 발사 준비 35351 stances).</param>
public sealed record BuffSkillSwap(uint Id, uint BuffId, int Priority, uint OriginSkillId, uint NewSkillId);

/// <summary>
/// Everything the four buff-grant tables say one buff grants its owner while it is active:
/// <c>buff_skills</c> and <c>buff_mount_skills</c> (skills), <c>buff_swap_skills</c> (action-bar
/// replacements) and <c>buff_passive_buffs</c> (passives). Built once per buff by
/// <c>SkillManager.Load</c>; rows whose buff, skill or passive is missing from the loaded content are
/// dropped there, so a set never names something that cannot be resolved.
/// </summary>
public sealed class BuffGrantSet
{
    /// <summary>Shared instance for the overwhelming majority of buffs, which grant nothing.</summary>
    public static readonly BuffGrantSet Empty = new();

    /// <summary><c>buff_skills.skill_id</c> plus <c>buff_mount_skills</c> resolved through
    /// <c>mount_skills.skill_id</c>.</summary>
    public IReadOnlyList<uint> GrantedSkills { get; init; } = [];

    public IReadOnlyList<BuffSkillSwap> Swaps { get; init; } = [];

    /// <summary><c>buff_passive_buffs.passive_buff_id</c>.</summary>
    public IReadOnlyList<uint> PassiveBuffIds { get; init; } = [];

    public bool IsEmpty => GrantedSkills.Count == 0 && Swaps.Count == 0 && PassiveBuffIds.Count == 0;

    public override string ToString() =>
        $"skills=[{string.Join(",", GrantedSkills)}] swaps=[{string.Join(",", Swaps.Select(s => $"{s.OriginSkillId}->{s.NewSkillId}(p{s.Priority})"))}] passives=[{string.Join(",", PassiveBuffIds)}]";
}
