namespace AAEmu.Game.Models.Tasks.Butlers;

public sealed class ButlerQuotaRefreshTask(Action refresh) : Task
{
    public override void Execute() => refresh();
}
