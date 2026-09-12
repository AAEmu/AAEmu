using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Items;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Models.Game.Mails;

/// <summary>
/// A caller-transaction delivery of already-persistent items. Planning and database writes leave
/// the live items untouched; the caller commits the transaction before applying the live move.
/// Disposing an uncommitted plan returns only its staged mail ids and metadata.
/// </summary>
public sealed class ExistingItemMailDeliveryPlan : IDisposable
{
    private readonly MailManager _manager;
    private readonly object _sync = new();
    private ExistingItemMailDeliveryState _state = ExistingItemMailDeliveryState.Prepared;

    internal ExistingItemMailDeliveryPlan(MailManager manager, IReadOnlyList<ExistingItemMailDeliveryBatch> batches)
    {
        _manager = manager;
        Batches = batches;
        Mails = Array.AsReadOnly(batches.Select(batch => batch.Mail).ToArray());
    }

    internal IReadOnlyList<ExistingItemMailDeliveryBatch> Batches { get; }

    /// <summary>The hidden letters that will be published together after the caller commits.</summary>
    internal IReadOnlyList<BaseMail> Mails { get; }

    /// <summary>Writes the planned mail and projected item rows on the caller's transaction.</summary>
    public bool TryPersistOn(MySqlConnection connection, MySqlTransaction transaction)
    {
        lock (_sync)
        {
            if (_state != ExistingItemMailDeliveryState.Prepared)
                return false;
            if (!_manager.TryPersistExistingItemDelivery(this, connection, transaction))
                return false;

            _state = ExistingItemMailDeliveryState.Persisted;
            return true;
        }
    }

    /// <summary>Applies the live item moves and publishes the letters after the transaction commits.</summary>
    public void Commit()
    {
        lock (_sync)
        {
            if (_state != ExistingItemMailDeliveryState.Persisted)
                throw new InvalidOperationException("An existing-item mail plan can commit only after its rows were persisted.");

            // Calling Commit asserts that the database transaction already committed. Never return
            // those ids to the allocator if an unexpected live-apply failure follows that point.
            _state = ExistingItemMailDeliveryState.Committing;
            try
            {
                _manager.CommitExistingItemDelivery(this);
                _state = ExistingItemMailDeliveryState.Committed;
            }
            catch
            {
                _state = ExistingItemMailDeliveryState.CommitFailed;
                throw;
            }
        }
    }

    /// <summary>Cancels an uncommitted plan without changing or releasing any attachment item.</summary>
    public void Rollback()
    {
        lock (_sync)
        {
            if (_state is ExistingItemMailDeliveryState.Committing or ExistingItemMailDeliveryState.Committed or
                ExistingItemMailDeliveryState.CommitFailed or ExistingItemMailDeliveryState.RolledBack)
                return;

            _manager.RollbackExistingItemDelivery(this);
            _state = ExistingItemMailDeliveryState.RolledBack;
        }
    }

    public void Dispose() => Rollback();
}

internal sealed record ExistingItemMailDeliveryBatch(
    BaseMail Mail,
    IReadOnlyList<ItemPersistenceSnapshot> ItemSnapshots,
    bool RestoreDeletedMailIdOnRollback);

internal enum ExistingItemMailDeliveryState
{
    Prepared,
    Persisted,
    Committing,
    Committed,
    CommitFailed,
    RolledBack
}
