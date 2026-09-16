namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// What a CancelOngoingBuff row asks to be removed from the target: <c>value1</c> names a buff tag and
/// <c>value2</c> a single buff id, one or the other, or neither.
/// </summary>
/// <remarks>
/// content, 10.0.2.13 game_decrypted: 995 <c>special_effects</c> rows of type 61, of which 977 are all
/// zero. The 18 rows that carry anything name 695 (11 rows), 1103 (3) and 1145 (1) in <c>value1</c> — all
/// three are real <c>tagged_buffs.tag_id</c> families, covering 60, 2 and 11 buffs — and 50153 in
/// <c>value2</c> (3 rows), which is neither a <c>buffs.id</c> nor a tag with any member. None of the 995
/// rows is referenced by <c>effects</c>, so no skill, buff or trigger can reach this special effect today:
/// the type is implemented to its data and stays inert until content names it.
/// </remarks>
public static class OngoingBuffCancelRules
{
    /// <summary>A cancel request in the ids <c>Buffs</c> removes by.</summary>
    public readonly record struct CancelRequest(uint BuffTagId, uint BuffId)
    {
        public bool IsNoOp => BuffTagId == 0 && BuffId == 0;
    }

    /// <summary>
    /// Reads the two value slots. Negative and zero both mean "this slot is not set"; every shipped row is
    /// zero or positive, so the clamp only keeps a malformed row from wrapping into a huge id.
    /// </summary>
    public static CancelRequest Resolve(int value1, int value2) =>
        new((uint)Math.Max(0, value1), (uint)Math.Max(0, value2));
}
