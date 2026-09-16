using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills;
using NLog;

namespace AAEmu.Game.Models.Tasks.Skills;

/// <summary>
/// Re-runs one aura buff's pulse until the aura ends.
/// </summary>
/// <remarks>
/// The pulse cannot ride the ordinary <see cref="DispelTask"/> cadence: that one is only rescheduled while
/// the buff authors a <c>tick</c>, and most auras do not (2105 지배자의 위엄2, 2332 테스트던전_누이여신상패시브
/// and 2422 태자의 원념 are all <c>tick 0</c>), so a tick-driven aura would apply its slave buff once and
/// never notice a unit walking in or out. The period is the aura's own <c>tick</c> when it has one, which is
/// what keeps a 1 s aura at 1 s.
/// </remarks>
public class AuraTask(Buff buff) : Task
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public WeakReference Effect = new(buff);

    public override void Execute()
    {
        if (!Effect.IsAlive || Effect.Target is not Buff aura || aura.Owner == null || aura.Template == null)
            return;

        if (aura.IsEnded())
        {
            aura.ReleaseAura();
            return;
        }

        try
        {
            aura.PulseAura();
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Aura pulse failed for buff {0} on unit {1}",
                aura.Template.BuffId, aura.Owner.ObjId);
        }

        if (!aura.IsEnded())
            EffectTaskManager.Instance.AddAuraTask(aura, AuraRules.PulseIntervalMs(aura.Template.Tick));
    }
}
