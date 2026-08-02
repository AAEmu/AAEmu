namespace AAEmu.Game.Models.Tasks.Skills;

/// <summary>Runs subsequent pulses of a repeated special effect without blocking the packet thread.</summary>
public sealed class SpecialEffectRepeatTask(Action execute) : Task
{
    public override void Execute() => execute();
}
