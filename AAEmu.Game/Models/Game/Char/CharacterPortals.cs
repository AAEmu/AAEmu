using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Packets.G2C;

using MySql.Data.MySqlClient;

using NLog;

namespace AAEmu.Game.Models.Game.Char;

public class CharacterPortals(Character owner)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private readonly object _saveSync = new();
    private ulong _deletionVersion;
    private Dictionary<uint, VisitedDistrict> VisitedDistricts { get; } = [];
    private readonly Dictionary<uint, ulong> _removedVisitedDistricts = [];
    private readonly Dictionary<uint, ulong> _removedPrivatePortals = [];

    public Dictionary<uint, Portal> PrivatePortals { get; set; } = [];
    public Dictionary<uint, Portal> DistrictPortals { get; set; } = [];
    public CharacterFavoritePortals FavoritePortals { get; } = new(owner);
    public Character Owner { get; set; } = owner;

    public Portal GetPortalInfo(uint id)
    {
        if (DistrictPortals.TryGetValue(id, out var info))
            return info;
        // Client may pass either wire id (district) or the return-point id stored in Type.
        foreach (var portal in DistrictPortals.Values)
        {
            if (portal.Type == id)
                return portal;
        }

        return PrivatePortals.TryGetValue(id, out var privatePortal) ? privatePortal : null;
    }

    public bool OwnsPortal(FavoritePortalRef favorite)
    {
        return favorite.PortalType switch
        {
            (byte)PortalBookType.Return => DistrictPortals.ContainsKey(favorite.PortalId),
            (byte)PortalBookType.Private => PrivatePortals.ContainsKey(favorite.PortalId),
            _ => false
        };
    }

    public bool TryUpdateFavorites(IReadOnlyCollection<FavoritePortalChange> changes)
    {
        int maximumFavorites;
        try
        {
            maximumFavorites = FavoritePortalCapacityRules.GetLimit(Owner);
        }
        catch
        {
            SendAuthoritativeState();
            throw;
        }

        return TryUpdateFavorites(changes, maximumFavorites);
    }

    internal bool TryUpdateFavorites(
        IReadOnlyCollection<FavoritePortalChange> changes,
        int maximumFavorites)
    {
        using var persistenceOperation = PersistenceOperationScope.Enter();
        lock (_saveSync)
        {
            if (!FavoritePortals.TryApply(changes, OwnsPortal, maximumFavorites))
            {
                SendAuthoritativeState();
                return false;
            }

            RefreshFavoriteFlags();
            return true;
        }
    }

    public void RemoveFromBookPortal(Portal portal, bool isPrivate)
    {
        if (PersistenceGate.IsSaveHeld)
            throw new InvalidOperationException("Portal removal cannot run inside a persistence save.");
        using var persistenceOperation = PersistenceOperationScope.Enter();
        lock (_saveSync)
        {
            if (isPrivate)
            {
                if (PrivatePortals.Remove(portal.Id))
                {
                    _removedPrivatePortals[portal.Id] = NextDeletionVersion();
                    FavoritePortals.Remove(new FavoritePortalRef((byte)PortalBookType.Private, portal.Id));
                    //Owner.SendMessage("Recorded Portal deleted.");
                }
            }
            else if (RemoveVisitedDistrictRecordCore(portal.SubZoneId))
            {
                FavoritePortals.Remove(new FavoritePortalRef((byte)PortalBookType.Return, portal.Id));
                PopulateDistrictPortals();
                //Owner.SendMessage("Default Portal deleted.");
            }

            RefreshFavoriteFlags();
        }
    }

    public void NotifySubZone(uint subZoneId)
    {
        if (PersistenceGate.IsSaveHeld)
            throw new InvalidOperationException("District notification cannot run inside a persistence save.");
        using var persistenceOperation = PersistenceOperationScope.Enter();
        lock (_saveSync)
        {
            if (VisitedDistricts.ContainsKey(subZoneId)) { return; }

            var portals = PortalManager.Instance.GetRecallBySubZoneId(subZoneId);
            if (portals == null) { return; }

            foreach (var portal in portals)
            {
                if (!VisitedDistricts.ContainsKey(subZoneId))
                {
                    var newVisitedDistrict = new VisitedDistrict
                    {
                        Id = VisitedSubZoneIdManager.Instance.GetNextId(), SubZone = subZoneId, Owner = Owner.Id
                    };
                    RestoreVisitedDistrictRecordCore(newVisitedDistrict);
                }
                PopulateDistrictPortals();
                Send();
                Logger.Debug($"{Owner.Name} - {portal.Name}:{subZoneId} added to return district list");
                Owner.SendDebugMessage($"{portal.Name}:{subZoneId} added to visited district list in the portal book");
            }
        }
    }

    public void AddPrivatePortal(float x, float y, float z, float zRot, uint zoneId, string name)
    {
        if (PersistenceGate.IsSaveHeld)
            throw new InvalidOperationException("Private portal creation cannot run inside a persistence save.");
        using var persistenceOperation = PersistenceOperationScope.Enter();
        lock (_saveSync)
        {
            // TODO - Only working by command
            var newPortal = new Portal
            {
                Id = PrivateBookIdManager.Instance.GetNextId(),
                Name = name,
                X = x,
                Y = y,
                Z = z,
                ZoneId = zoneId,
                ZRot = zRot,
                Owner = Owner.Id
            };
            PrivatePortals.Add(newPortal.Id, newPortal);
            Owner.SendPacket(new SCCharacterPortalsPacket([newPortal]));
        }
    }

    public bool ChangePrivatePortalName(uint id, string name)
    {
        if (PersistenceGate.IsSaveHeld)
            throw new InvalidOperationException("Private portal rename cannot run inside a persistence save.");
        using var persistenceOperation = PersistenceOperationScope.Enter();
        lock (_saveSync)
        {
            if (PrivatePortals.TryGetValue(id, out var privatePortal))
            {
                privatePortal.Name = name;
                Owner.SendPacket(new SCPortalInfoSavedPacket(privatePortal));
                return true;
            }

            return false;
        }
    }

    public void Send()
    {
        lock (_saveSync)
        {
            if (PrivatePortals.Count > 0)
            {
                var portals = FavoritePortals.BuildFlaggedPortals(PrivatePortals.Values, PortalBookType.Private).ToArray();
                Owner.SendPacket(new SCCharacterPortalsPacket(portals));
            }

            if (DistrictPortals.Count > 0)
            {
                var portals = FavoritePortals.BuildFlaggedPortals(DistrictPortals.Values, PortalBookType.Return).ToArray();
                // Trailing field is the bound district id (client name returnDistrictId), not the return-point id.
                Owner.SendPacket(new SCCharacterReturnDistrictsPacket(portals, Owner.ReturnDistrictId));
            }
        }
    }

    protected virtual void SendAuthoritativeState() => Send();

    public void Load(MySqlConnection connection)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM portal_book_coords WHERE `owner` = @owner";
            command.Parameters.AddWithValue("@owner", Owner.Id);
            command.Prepare();
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var template = new Portal
                    {
                        Id = reader.GetUInt32("id"),
                        Name = reader.GetString("name"),
                        X = reader.GetFloat("x"),
                        Y = reader.GetFloat("y"),
                        Z = reader.GetFloat("z"),
                        ZoneId = reader.GetUInt32("zone_id"),
                        ZRot = reader.GetFloat("z_rot"),
                        SubZoneId = reader.GetUInt32("sub_zone_id"),
                        Owner = reader.GetUInt32("owner")
                    };
                    PrivatePortals.Add(template.Id, template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM portal_visited_district WHERE `owner` = @owner";
            command.Parameters.AddWithValue("@owner", Owner.Id);
            command.Prepare();
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var template = new VisitedDistrict
                    {
                        Id = reader.GetUInt32("id"),
                        SubZone = reader.GetUInt32("subzone"),
                        Owner = reader.GetUInt32("owner")
                    };
                    VisitedDistricts.Add(template.SubZone, template);
                }
            }
        }

        PopulateDistrictPortals();
        FavoritePortals.Load(connection, OwnsPortal);
        RefreshFavoriteFlags();
    }

    public ulong Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        lock (_saveSync)
        {
            var saveVersion = _deletionVersion;
            var removedVisited = _removedVisitedDistricts.Keys.ToArray();
            var removedPrivate = _removedPrivatePortals.Keys.ToArray();

            if (removedVisited.Length > 0)
            {
                using var command = connection.CreateCommand();
                command.Connection = connection;
                command.Transaction = transaction;
                command.CommandText =
                    "DELETE FROM portal_visited_district WHERE owner = @owner AND subzone IN(" +
                    string.Join(",", removedVisited) + ")";
                command.Parameters.AddWithValue("@owner", Owner.Id);
                command.Prepare();
                command.ExecuteNonQuery();
            }

            if (removedPrivate.Length > 0)
            {
                using var command = connection.CreateCommand();
                command.Connection = connection;
                command.Transaction = transaction;
                command.CommandText =
                    "DELETE FROM portal_book_coords WHERE owner = @owner AND id IN(" +
                    string.Join(",", removedPrivate) + ")";
                command.Parameters.AddWithValue("@owner", Owner.Id);
                command.Prepare();
                command.ExecuteNonQuery();
            }

            foreach (var (_, value) in PrivatePortals)
            {
                using var command = connection.CreateCommand();
                command.Connection = connection;
                command.Transaction = transaction;
                command.CommandText =
                    "REPLACE INTO portal_book_coords(`id`,`name`,`x`,`y`,`z`,`zone_id`,`z_rot`,`sub_zone_id`,`owner`) " +
                    "VALUES (@id, @name, @x, @y, @z, @zone_id, @z_rot, @sub_zone_id, @owner)";
                command.Parameters.AddWithValue("@id", value.Id);
                command.Parameters.AddWithValue("@name", value.Name);
                command.Parameters.AddWithValue("@x", value.X);
                command.Parameters.AddWithValue("@y", value.Y);
                command.Parameters.AddWithValue("@z", value.Z);
                command.Parameters.AddWithValue("@zone_id", value.ZoneId);
                command.Parameters.AddWithValue("@z_rot", value.ZRot);
                command.Parameters.AddWithValue("@sub_zone_id", value.SubZoneId);
                command.Parameters.AddWithValue("@owner", value.Owner);
                command.ExecuteNonQuery();
            }

            foreach (var (_, value) in VisitedDistricts)
            {
                using var command = connection.CreateCommand();
                command.Connection = connection;
                command.Transaction = transaction;
                command.CommandText =
                    "REPLACE INTO portal_visited_district(`id`,`subzone`,`owner`) VALUES (@id, @subzone, @owner)";
                command.Parameters.AddWithValue("@id", value.Id);
                command.Parameters.AddWithValue("@subzone", value.SubZone);
                command.Parameters.AddWithValue("@owner", value.Owner);
                command.ExecuteNonQuery();
            }

            FavoritePortals.Save(connection, transaction);
            return saveVersion;
        }
    }

    public void OnSaveCommitted(ulong saveVersion)
    {
        lock (_saveSync)
        {
            RemoveCommittedDeletions(_removedVisitedDistricts, saveVersion);
            RemoveCommittedDeletions(_removedPrivatePortals, saveVersion);
        }
    }

    internal ulong PendingDeletionVersion
    {
        get { lock (_saveSync) return _deletionVersion; }
    }

    internal int PendingDeletedPortalCount
    {
        get { lock (_saveSync) return _removedPrivatePortals.Count; }
    }

    internal int PendingDeletedDistrictCount
    {
        get { lock (_saveSync) return _removedVisitedDistricts.Count; }
    }

    internal bool HasPendingDeletedPortal(uint id)
    {
        lock (_saveSync)
            return _removedPrivatePortals.ContainsKey(id);
    }

    internal bool RemoveVisitedDistrictRecord(uint subZoneId)
    {
        if (PersistenceGate.IsSaveHeld)
            throw new InvalidOperationException("Visited-district removal cannot run inside a persistence save.");
        using var persistenceOperation = PersistenceOperationScope.Enter();
        lock (_saveSync)
            return RemoveVisitedDistrictRecordCore(subZoneId);
    }

    internal void RestoreVisitedDistrictRecord(VisitedDistrict district)
    {
        ArgumentNullException.ThrowIfNull(district);
        if (PersistenceGate.IsSaveHeld)
            throw new InvalidOperationException("Visited-district restore cannot run inside a persistence save.");
        using var persistenceOperation = PersistenceOperationScope.Enter();
        lock (_saveSync)
            RestoreVisitedDistrictRecordCore(district);
    }

    internal bool HasVisitedDistrictRecord(uint subZoneId)
    {
        lock (_saveSync)
            return VisitedDistricts.ContainsKey(subZoneId);
    }

    private bool RemoveVisitedDistrictRecordCore(uint subZoneId)
    {
        if (!VisitedDistricts.Remove(subZoneId))
            return false;

        _removedVisitedDistricts[subZoneId] = NextDeletionVersion();
        return true;
    }

    private void RestoreVisitedDistrictRecordCore(VisitedDistrict district)
    {
        VisitedDistricts[district.SubZone] = district;
        _removedVisitedDistricts.Remove(district.SubZone);
    }

    private ulong NextDeletionVersion()
    {
        return ++_deletionVersion;
    }

    private static void RemoveCommittedDeletions(Dictionary<uint, ulong> pending, ulong saveVersion)
    {
        foreach (var id in pending.Where(pair => pair.Value <= saveVersion).Select(pair => pair.Key).ToArray())
            pending.Remove(id);
    }

    private void PopulateDistrictPortals()
    {
        DistrictPortals.Clear();
        if (VisitedDistricts.Count <= 0) { return; }

        foreach (var subZone in VisitedDistricts)
        {
            var portals = PortalManager.Instance.GetRecallBySubZoneId(subZone.Key);
            if (portals == null || portals.Count == 0) { continue; }

            foreach (var portal in portals)
            {
                // recalls.json Id == return_point_id. The client book entry uses district_id as
                // wire id and return_point_id as wire type (SC 0x089 capture: id=district, type=240).
                var districtId = PortalManager.Instance.GetDistrictIdByReturnPoint(portal.Id, Owner.Faction.Id);
                if (districtId == 0)
                    districtId = portal.Id;

                var entry = new Portal
                {
                    Id = districtId,
                    Type = portal.Id,
                    Name = portal.Name,
                    X = portal.X,
                    Y = portal.Y,
                    Z = portal.Z,
                    ZoneId = portal.ZoneId,
                    ZRot = portal.ZRot,
                    SubZoneId = portal.SubZoneId,
                    Owner = Owner.Id
                };
                DistrictPortals.TryAdd(entry.Id, entry);
            }
        }

        RefreshFavoriteFlags();
    }

    private void RefreshFavoriteFlags()
    {
        foreach (var portal in PrivatePortals.Values)
            portal.IsFavorite = FavoritePortals.Contains(new FavoritePortalRef((byte)PortalBookType.Private, portal.Id));
        foreach (var portal in DistrictPortals.Values)
            portal.IsFavorite = FavoritePortals.Contains(new FavoritePortalRef((byte)PortalBookType.Return, portal.Id));
    }
}

internal sealed class PortalSaveCommitToken(ulong version)
{
    public ulong Version { get; } = version;
}

internal sealed class PortalSaveCommitCoordinator
{
    private readonly object _sync = new();
    private PortalSaveCommitToken? _pending;

    public PortalSaveCommitToken? Complete(ulong saveVersion, bool characterSaveSucceeded)
    {
        if (!characterSaveSucceeded)
            return null;

        var token = new PortalSaveCommitToken(saveVersion);
        lock (_sync)
            _pending = token;
        return token;
    }

    public bool Confirm(PortalSaveCommitToken? token)
    {
        if (token == null)
            return false;

        lock (_sync)
        {
            if (!ReferenceEquals(_pending, token))
                return false;
            _pending = null;
        }
        return true;
    }

    public bool Discard(PortalSaveCommitToken? token)
    {
        if (token == null)
            return false;

        lock (_sync)
        {
            if (!ReferenceEquals(_pending, token))
                return false;
            _pending = null;
        }
        return true;
    }
}

internal static class HeroCharacterSaveRules
{
    internal delegate bool SaveOperation(out PortalSaveCommitToken? token);

    public static bool TrySave(SaveOperation save, out PortalSaveCommitToken? token)
    {
        ArgumentNullException.ThrowIfNull(save);
        if (!save(out token))
        {
            token = null;
            return false;
        }

        return true;
    }
}
