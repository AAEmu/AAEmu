using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
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

    /// <summary>
    /// 제압당함 (subdued, 5 s) - the state the player arrest skill 20323 puts under the one-second
    /// 체포 중..2 hold, where the bot arrest 20387 applies <see cref="UnderArrestBuff"/> alone.
    /// </summary>
    public const uint SubduedBuff = 4883;

    public const uint ForcedMoveToCourtBuff = 8038;

    /// <summary>Every buff that means "this character is under arrest right now".</summary>
    public static readonly uint[] ArrestStateBuffIds = [UnderArrestBuff, UnderArrestShortBuff, SubduedBuff];

    /// <summary>
    /// How long an unanswered imprison-or-trial offer stays open. Ignoring the dialog must not leave the
    /// courthouse state on a character for the rest of its ten-hour duration.
    /// </summary>
    public const int OfferSeconds = 60;

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
    public static bool IsArrestStateBuff(uint buffId) => ArrestStateBuffIds.Contains(buffId);

    /// <summary>
    /// True for a character the court summons on death. The shipped wanted buff is what the crime-point
    /// threshold applies (see <c>Character.CheckWantedThreshold</c>), and a wanted death is the
    /// courthouse's own summons: the resurrection lands them there as the defendant.
    /// </summary>
    public static bool IsWanted(Character character) =>
        character != null && character.Buffs.CheckBuff((uint)BuffConstants.Wanted);

    /// <summary>
    /// How much of the arrest state on a unit is still to run. An arrest skill can put more than one
    /// state on its target - the player arrest stacks a five-second subdual under a one-second hold -
    /// so the longest one left is what the escort has to wait for. Buffs that are not arrest states are
    /// ignored, and a missing remainder counts as run out.
    /// </summary>
    public static TimeSpan LongestArrestStateLeft(IEnumerable<Buff> buffs)
    {
        var longest = TimeSpan.Zero;
        foreach (var buff in buffs ?? [])
        {
            if (buff?.Template == null || !IsArrestStateBuff(buff.Template.BuffId))
                continue;

            var left = TimeSpan.FromMilliseconds(Math.Max(0, buff.GetTimeLeft()));
            if (left > longest)
                longest = left;
        }

        return longest;
    }

    /// <summary>Nuia-side characters are escorted to the Marianople courthouse, everyone else east.</summary>
    public static (float X, float Y, float Z) CourtPositionFor(FactionsEnum alliance) =>
        alliance == FactionsEnum.NuiaAlliance ? MarianopleCourt : EasternCourt;
}
