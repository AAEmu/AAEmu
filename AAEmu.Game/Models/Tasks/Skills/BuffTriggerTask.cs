namespace AAEmu.Game.Models.Tasks.Skills;

/// <summary>
/// Applies a buff trigger's effect after its authored <c>buff_triggers.delay_time</c>, without blocking the
/// thread the triggering event was raised on.
/// </summary>
public sealed class BuffTriggerTask(Action applyEffect) : Task
{
    public override void Execute() => applyEffect();
}