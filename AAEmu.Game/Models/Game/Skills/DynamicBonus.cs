using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// A bonus whose value depends on the elapsed lifetime of its source buff.
/// Unlike a static <see cref="Bonus"/>, it is NOT snapshotted at apply time: its value is
/// recomputed every time the owning attribute is read (see Unit.CalculateWithBonuses).
///
/// The backing <see cref="LinearFuncTemplate"/> is pre-resolved once (at buff Start) so the hot
/// evaluation path performs no SkillManager lookup and no string allocation.
/// </summary>
public class DynamicBonus
{
    public DynamicBonusTemplate Template { get; init; }
    public Buff SourceBuff { get; init; }
    public LinearFuncTemplate LinearFunc { get; init; }

    /// <summary>
    /// Computes the current value of this dynamic bonus.
    /// Returns false when the bonus cannot be evaluated (missing data, unsupported func type),
    /// in which case it must NOT be applied.
    /// </summary>
    public bool Evaluate(out double value)
    {
        value = 0d;

        if (SourceBuff == null || Template == null)
            return false;

        switch (Template.FuncType)
        {
            case "LinearFunc":
            {
                if (LinearFunc == null)
                    return false;

                // ratio = elapsed / duration -> 0 at start (StartValue), 1 at expiry (EndValue).
                // A non-expiring buff (Duration <= 0) is treated as fully elapsed (EndValue),
                // matching the prototype behavior; no real LinearFunc dynamic_unit_modifier is
                // expected on a permanent buff.
                var duration = SourceBuff.Duration;
                var ratio = 1d;
                if (duration > 0)
                {
                    var elapsed = (DateTime.UtcNow - SourceBuff.StartTime).TotalMilliseconds;
                    ratio = Math.Clamp(elapsed / duration, 0d, 1d);
                }

                value = LinearFunc.GetValue(ratio);
                return true;
            }

            case "ManualFunc":
                // Not implemented: the expected client behavior is unknown. Do not apply a value.
                return false;

            default:
                return false;
        }
    }
}
