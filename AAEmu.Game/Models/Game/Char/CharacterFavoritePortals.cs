using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Models.Game.Char;

/// <summary>
/// Durable favorite membership for a character's private and return-district portal books.
/// The client owns the immediate checkbox state; this store is the authoritative relog source.
/// It preserves add order in storage while leaving the portal book's own row order unchanged.
/// </summary>
public sealed class CharacterFavoritePortals
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private readonly object _sync = new();
    private readonly IFavoritePortalPersistence _persistence;
    private FavoritePortalState _state = FavoritePortalState.Empty;

    public CharacterFavoritePortals(Character owner)
        : this(owner, new MySqlFavoritePortalPersistence())
    {
    }

    internal CharacterFavoritePortals(Character owner, IFavoritePortalPersistence persistence)
    {
        Owner = owner;
        _persistence = persistence;
    }

    public Character Owner { get; }

    public IReadOnlyList<Portal> BuildFlaggedPortals(IEnumerable<Portal> portals, PortalBookType portalType)
    {
        lock (_sync)
            return _state.BuildFlaggedPortals(portals, portalType);
    }

    public bool Contains(FavoritePortalRef favorite)
    {
        lock (_sync)
            return _state.Contains(favorite);
    }

    public void Load(
        MySqlConnection connection,
        Func<FavoritePortalRef, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(exists);

        var loaded = new List<FavoritePortalRef>();
        var seen = new HashSet<FavoritePortalRef>();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT portal_type, portal_id FROM character_favorite_portals " +
            "WHERE owner = @owner ORDER BY sort_order, portal_type, portal_id";
        command.Parameters.AddWithValue("@owner", Owner.Id);
        command.Prepare();

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var favorite = new FavoritePortalRef(
                reader.GetByte("portal_type"),
                reader.GetUInt32("portal_id"));
            if (!favorite.IsValid || !exists(favorite))
            {
                Logger.Warn(
                    "Ignoring unknown favorite portal type={0} id={1} for character {2}",
                    favorite.PortalType,
                    favorite.PortalId,
                    Owner.Id);
                continue;
            }

            if (!seen.Add(favorite))
            {
                Logger.Warn(
                    "Ignoring duplicate favorite portal type={0} id={1} for character {2}",
                    favorite.PortalType,
                    favorite.PortalId,
                    Owner.Id);
                continue;
            }

            loaded.Add(favorite);
        }

        LoadState(loaded, exists);
    }

    internal void LoadState(
        IEnumerable<FavoritePortalRef> favorites,
        Func<FavoritePortalRef, bool> exists)
    {
        if (!FavoritePortalState.TryCreate(favorites, exists, out var state))
            throw new InvalidOperationException("Favorite portal state failed validation while loading.");

        lock (_sync)
            _state = state;
    }

    public bool TryApply(
        IReadOnlyCollection<FavoritePortalChange> changes,
        Func<FavoritePortalRef, bool> exists,
        int maximumFavorites)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(exists);

        if (PersistenceGate.IsSaveHeld)
        {
            Logger.Error("Favorite portal update rejected inside a persistence save for character {0}", Owner.Id);
            return false;
        }

        using var persistenceOperation = PersistenceOperationScope.Enter();
        lock (_sync)
        {
            if (!_state.TryApply(changes, exists, maximumFavorites, out var next))
                return false;

            try
            {
                _persistence.PersistNow(Owner.Id, next.Favorites);
                _state = next;
                return true;
            }
            catch (Exception exception)
            {
                Logger.Error(
                    exception,
                    "Failed to update favorite portals for character {0}",
                    Owner.Id);
                return false;
            }
        }
    }

    public void Remove(FavoritePortalRef favorite)
    {
        if (PersistenceGate.IsSaveHeld)
            throw new InvalidOperationException("Favorite portal removal cannot run inside a persistence save.");

        using var persistenceOperation = PersistenceOperationScope.Enter();
        lock (_sync)
            _state = _state.Without(favorite);
    }

    public void Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);

        // Keep the transaction write under the same lock as PersistNow. Otherwise a live update
        // can commit and then this older character-save snapshot can overwrite it.
        lock (_sync)
            _persistence.Save(Owner.Id, connection, transaction, _state.Favorites);
    }
}

internal interface IFavoritePortalPersistence
{
    void PersistNow(uint ownerId, IReadOnlyList<FavoritePortalRef> favorites);
    void Save(
        uint ownerId,
        MySqlConnection connection,
        MySqlTransaction transaction,
        IReadOnlyList<FavoritePortalRef> favorites);
}

internal sealed class MySqlFavoritePortalPersistence : IFavoritePortalPersistence
{
    public void PersistNow(uint ownerId, IReadOnlyList<FavoritePortalRef> favorites)
    {
        if (!PersistenceGate.IsOperationHeld || PersistenceGate.IsSaveHeld)
            throw new InvalidOperationException("Immediate favorite portal persistence requires a live operation scope.");

        using var connection = MySQL.CreateConnection();
        using var transaction = connection.BeginTransaction();
        try
        {
            Save(ownerId, connection, transaction, favorites);
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public void Save(
        uint ownerId,
        MySqlConnection connection,
        MySqlTransaction transaction,
        IReadOnlyList<FavoritePortalRef> favorites)
    {
        using (var delete = connection.CreateCommand())
        {
            delete.Connection = connection;
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM character_favorite_portals WHERE owner = @owner";
            delete.Parameters.AddWithValue("@owner", ownerId);
            delete.ExecuteNonQuery();
        }

        using var insert = connection.CreateCommand();
        insert.Connection = connection;
        insert.Transaction = transaction;
        insert.CommandText =
            "INSERT INTO character_favorite_portals(owner, portal_type, portal_id, sort_order) " +
            "VALUES (@owner, @portalType, @portalId, @sortOrder)";
        var ownerParameter = insert.Parameters.Add("@owner", MySqlDbType.UInt32);
        var typeParameter = insert.Parameters.Add("@portalType", MySqlDbType.UInt32);
        var idParameter = insert.Parameters.Add("@portalId", MySqlDbType.UInt32);
        var orderParameter = insert.Parameters.Add("@sortOrder", MySqlDbType.UInt32);
        ownerParameter.Value = ownerId;
        insert.Prepare();

        for (var index = 0; index < favorites.Count; index++)
        {
            var favorite = favorites[index];
            typeParameter.Value = favorite.PortalType;
            idParameter.Value = favorite.PortalId;
            orderParameter.Value = (uint)index;
            if (insert.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("Favorite portal insert did not affect exactly one row.");
        }
    }
}
