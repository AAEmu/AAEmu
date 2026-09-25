using AAEmu.Game.Models.Game.Butlers;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Trading;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Core.Managers;

public sealed class MySqlButlerSpecialtyTradePersistence(
    IButlerRepository repository,
    IItemManager itemManager,
    ISpecialtyMarketStore marketStore,
    Func<MySqlConnection> openConnection) : IButlerSpecialtyTradePersistence
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public ButlerSpecialtyTradePersistResult Register(
        CharacterButlerRecord proposed,
        ButlerSpecialtyTradeJobCandidate candidate,
        IReadOnlyList<ItemPersistenceSnapshot> itemSnapshots)
    {
        long jobId = 0;
        var commitAttempted = false;
        try
        {
            using var connection = openConnection();
            using var transaction = connection.BeginTransaction();
            repository.Save(proposed, connection, transaction);
            itemManager.PersistSnapshots(connection, transaction, itemSnapshots);
            jobId = repository.InsertSpecialtyTradeJob(proposed.CharacterId, candidate, connection, transaction);
            commitAttempted = true;
            transaction.Commit();
            return new ButlerSpecialtyTradePersistResult(true, false, jobId);
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to persist specialty-trade registration for character {0}",
                proposed.CharacterId);
            return new ButlerSpecialtyTradePersistResult(false, commitAttempted, jobId);
        }
    }

    public ButlerSpecialtyTradePersistResult Cancel(uint characterId, long jobId)
    {
        var commitAttempted = false;
        try
        {
            using var connection = openConnection();
            using var transaction = connection.BeginTransaction();
            if (!repository.DeleteSpecialtyTradeJob(characterId, jobId, connection, transaction))
            {
                transaction.Rollback();
                return new ButlerSpecialtyTradePersistResult(false, false, jobId);
            }
            commitAttempted = true;
            transaction.Commit();
            return new ButlerSpecialtyTradePersistResult(true, false, jobId);
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to persist specialty-trade cancellation for character {0}, job {1}",
                characterId, jobId);
            return new ButlerSpecialtyTradePersistResult(false, commitAttempted, jobId);
        }
    }

    public ButlerSpecialtyTradePersistResult Settle(
        uint characterId,
        long jobId,
        SpecialtyMarketWrite market)
    {
        ArgumentNullException.ThrowIfNull(market);
        var commitAttempted = false;
        try
        {
            using var connection = openConnection();
            using var transaction = connection.BeginTransaction();
            if (!repository.DeleteSpecialtyTradeJob(characterId, jobId, connection, transaction))
            {
                transaction.Rollback();
                return new ButlerSpecialtyTradePersistResult(false, false, jobId);
            }
            marketStore.Apply(connection, transaction, market);
            commitAttempted = true;
            transaction.Commit();
            return new ButlerSpecialtyTradePersistResult(true, false, jobId);
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to persist specialty-trade settlement for character {0}, job {1}",
                characterId, jobId);
            return new ButlerSpecialtyTradePersistResult(false, commitAttempted, jobId);
        }
    }

    public bool TryLoadSpecialtyTradeJob(uint characterId, long jobId, out ButlerSpecialtyTradeJob job) =>
        repository.TryLoadSpecialtyTradeJob(characterId, jobId, out job);
}
