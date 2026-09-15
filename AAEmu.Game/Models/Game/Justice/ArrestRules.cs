using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.Justice;

/// <summary>
/// Arrest -> court -> sentence rules. Every id here comes from the shipped crime data: buffs
/// 4906 체포 중.. (15 s) and 4962 체포 중..2 are the states the arrest skills 20323 / 20387 put on the
/// criminal, 8038 재판장 강제 이동 (forced move to court) is the courthouse state, and 2028 수감자
/// (prisoner, 30 min) is the sentence - its own timeout trigger already returns the prisoner to their
/// recall point, so nothing has to release them by hand.
/// </summary>
public static class ArrestRules
{
    public const uint UnderArrestBuff = 4906;
    public const uint UnderArrestShortBuff = 4962;
    public const uint ForcedMoveToCourtBuff = 8038;

    /// <summary>
    /// 배심원(앉기 버프) 25977 - the pose a seated juror holds. Like the serving buff in
    /// <see cref="Skills.BuffConstants.Juror"/> it ships with no duration at all, so whoever seats a
    /// juror has to take it off again or they keep the pose for the rest of the session.
    /// </summary>
    public const uint SeatedJurorBuff = 25977;

    /// <summary>
    /// 범죄 점수 감소 25281 carries a single special effect (15906) worth -100 crime points. That is
    /// the shipped way a crime is paid off, and it is what clears the wanted state once a sentence has
    /// been served - the wanted buff falls away by itself when the points drop under
    /// <see cref="Core.Managers.CrimeManager.WantedCrimePointThreshold"/>.
    /// </summary>
    public const short ServedCrimePointReduction = -100;

    /// <summary>
    /// The Marianople jail sentence (buff 631 수감자, 30 min). Its own timeout trigger returns the
    /// prisoner to the release spot, so nothing has to let them out by hand. The other 수감자 row,
    /// 2028, is the Punishment Island exile and is not used for a normal crime sentence.
    /// </summary>
    public const uint PrisonerBuff = 631;

    /// <summary>
    /// Return point of the Marianople jail interior - return_points 47 "mari_prisonin"
    /// (마리아노플 감옥내부). Its partner 48 "mari_prisonout" is where the sentence ends.
    /// </summary>
    public const uint JailReturnPointId = 47;

    /// <summary>Where a finished sentence puts the prisoner - return_points 48 "mari_prisonout".</summary>
    public const uint PrisonExitReturnPointId = 48;

    /// <summary>The shipped sentence length: both 수감자 buffs (631 Marianople, 2028 exile) run thirty minutes.</summary>
    public const uint SentenceMinutes = 30;

    // Courthouse markers in main_world (doodads 재판소 6122-6124). World position is
    // cell * 1024 + cell-local position: 6123/6124 live in cell 010_011 (Marianople),
    // 6122 in cell 016_008 (the eastern continent).
    private static readonly (float X, float Y, float Z) MarianopleCourt = (11098.5f, 12069.5f, 145.677f);
    private static readonly (float X, float Y, float Z) EasternCourt = (17024f, 8905f, 157f);

    /// <summary>True for the buffs that mean "this character is under arrest right now".</summary>
    public static bool IsArrestStateBuff(uint buffId) =>
        buffId is UnderArrestBuff or UnderArrestShortBuff;

    /// <summary>Nuia-side characters are escorted to the Marianople courthouse, everyone else east.</summary>
    public static (float X, float Y, float Z) CourtPositionFor(FactionsEnum alliance) =>
        alliance == FactionsEnum.NuiaAlliance ? MarianopleCourt : EasternCourt;
}
