using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Tasks.Butlers;
using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>Keeps online Farmhand quota counters aligned with the server calendar.</summary>
public sealed class ButlerQuotaRefreshService(
    IButlerChargeService chargeService,
    IWorldManager worldManager,
    ITaskManager taskManager) : IInitializable
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(1);

    public void Initialize()
    {
        taskManager.Schedule(
            new ButlerQuotaRefreshTask(RefreshOnlineCharacters),
            RefreshInterval,
            RefreshInterval);
    }

    private void RefreshOnlineCharacters()
    {
        foreach (var character in worldManager.GetAllCharacters())
        {
            try
            {
                var result = chargeService.RefreshQuotaPeriods(character);
                if (!result.Success)
                    Logger.Warn("Could not refresh farmhand quotas for character {0}: {1}",
                        character.Id, result.Failure);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to refresh farmhand quotas for character {0}", character.Id);
            }
        }
    }
}
