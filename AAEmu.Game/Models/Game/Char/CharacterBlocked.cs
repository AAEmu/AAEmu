using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Models.Game.Char;

public class CharacterBlocked(Character owner)
{
    public const int MaxBlockedUsers = 200;
    public const sbyte LocalWorldId = -1;

    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private readonly object _blockedLock = new();

    public Character Owner { get; set; } = owner;
    public Dictionary<uint, BlockedTemplate> BlockedList { get; set; } = [];

    public bool Contains(uint characterId)
    {
        lock (_blockedLock)
            return BlockedList.ContainsKey(characterId);
    }

    public void Send()
    {
        uint[] blockedIds;
        lock (_blockedLock)
            blockedIds = [.. BlockedList.Keys];

        var allBlocked = GetBlockedInfo(blockedIds);
        if (allBlocked.Length == 0)
        {
            Owner.SendPacket(new SCBlockedUsersPacket(0, []));
            return;
        }

        foreach (var page in allBlocked.Chunk(SCBlockedUsersPacket.MaxCountPerPacket))
            Owner.SendPacket(new SCBlockedUsersPacket(allBlocked.Length, page));
    }

    public void Load(MySqlConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM blocked WHERE `owner` = @owner";
        command.Parameters.AddWithValue("@owner", Owner.Id);
        command.Prepare();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var template = new BlockedTemplate
            {
                Owner = reader.GetUInt32("owner"),
                BlockedId = reader.GetUInt32("blocked_id")
            };
            lock (_blockedLock)
                BlockedList.Add(template.BlockedId, template);
        }
    }

    public void Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        // Add/remove operations are committed immediately. A later snapshot write could otherwise resurrect
        // an entry removed concurrently with the general character save.
    }

    public void AddBlockedUser(string name, sbyte worldId)
    {
        name = name?.Trim();
        var target = string.IsNullOrEmpty(name) ? null : FindBlockedTarget(name, worldId);
        if (target == null)
        {
            SendAddFailure(name ?? string.Empty, worldId, ErrorMessageType.UserNotExist);
            return;
        }

        if (target.CharacterId == Owner.Id)
        {
            Owner.SendPacket(new SCAddBlockedUserPacket(target, false, ErrorMessageType.CannotBlockUserSelf));
            return;
        }

        lock (_blockedLock)
        {
            if (BlockedList.ContainsKey(target.CharacterId))
            {
                Owner.SendPacket(new SCAddBlockedUserPacket(
                    target, false, ErrorMessageType.CannotAddExistingMember));
                return;
            }

            if (BlockedList.Count >= MaxBlockedUsers)
            {
                Owner.SendPacket(new SCAddBlockedUserPacket(target, false, ErrorMessageType.BlockUserMax));
                return;
            }

            try
            {
                using var connection = MySQL.CreateConnection();
                using var command = connection.CreateCommand();
                command.CommandText = "INSERT INTO blocked(`owner`,`blocked_id`) VALUES (@owner, @blocked_id)";
                command.Parameters.AddWithValue("@owner", Owner.Id);
                command.Parameters.AddWithValue("@blocked_id", target.CharacterId);
                if (command.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("The block-list insert did not affect exactly one row.");
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Failed to block character {0} for {1}", target.CharacterId, Owner.Id);
                Owner.SendPacket(new SCAddBlockedUserPacket(target, false, ErrorMessageType.BlockUser));
                return;
            }

            BlockedList.Add(target.CharacterId, new BlockedTemplate
            {
                BlockedId = target.CharacterId,
                Owner = Owner.Id
            });
        }

        Owner.SendPacket(new SCAddBlockedUserPacket(target, true, ErrorMessageType.NoErrorMessage));
    }

    public void RemoveBlockedUser(ulong blockedCharacterId)
    {
        if (blockedCharacterId > uint.MaxValue)
        {
            Owner.SendPacket(new SCDeleteBlockedUserPacket(
                blockedCharacterId, false, ErrorMessageType.UnblockUser));
            return;
        }

        var internalCharacterId = (uint)blockedCharacterId;
        lock (_blockedLock)
        {
            if (!BlockedList.ContainsKey(internalCharacterId))
            {
                Owner.SendPacket(new SCDeleteBlockedUserPacket(
                    blockedCharacterId, false, ErrorMessageType.UnblockUser));
                return;
            }

            try
            {
                using var connection = MySQL.CreateConnection();
                using var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM blocked WHERE owner = @owner AND blocked_id = @blocked_id";
                command.Parameters.AddWithValue("@owner", Owner.Id);
                command.Parameters.AddWithValue("@blocked_id", internalCharacterId);
                if (command.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("The block-list delete did not affect exactly one row.");
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Failed to unblock character {0} for {1}", internalCharacterId, Owner.Id);
                Owner.SendPacket(new SCDeleteBlockedUserPacket(
                    blockedCharacterId, false, ErrorMessageType.UnblockUser));
                return;
            }

            BlockedList.Remove(internalCharacterId);
        }

        Owner.SendPacket(new SCDeleteBlockedUserPacket(
            blockedCharacterId, true, ErrorMessageType.NoErrorMessage));
    }

    private Blocked[] GetBlockedInfo(uint[] ids)
    {
        var blocked = new List<Blocked>(ids.Length);
        var offlineIds = new List<uint>();
        foreach (var id in ids)
        {
            var character = WorldManager.Instance.GetCharacterById(id);
            if (character == null)
            {
                offlineIds.Add(id);
                continue;
            }

            blocked.Add(FormatBlocked(character.Id, character.Name, character.Transform.WorldId));
        }

        if (offlineIds.Count == 0)
            return [.. blocked];

        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, name, world_id FROM characters WHERE id IN(" + string.Join(",", offlineIds) + ")";
        command.Prepare();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            blocked.Add(FormatBlocked(
                reader.GetUInt32("id"),
                reader.GetString("name"),
                reader.GetUInt32("world_id")));
        }

        return [.. blocked];
    }

    private Blocked FindBlockedTarget(string name, sbyte wireWorldId)
    {
        var worldId = ResolveWorldId(wireWorldId);
        var online = WorldManager.Instance.GetCharacter(name);
        if (online != null && online.Transform.WorldId == worldId)
            return FormatBlocked(online.Id, online.Name, online.Transform.WorldId);

        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, name, world_id FROM characters WHERE `name` = @name AND world_id = @world_id LIMIT 1";
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@world_id", worldId);
        command.Prepare();
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? FormatBlocked(reader.GetUInt32("id"), reader.GetString("name"), reader.GetUInt32("world_id"))
            : null;
    }

    private uint ResolveWorldId(sbyte wireWorldId)
    {
        return wireWorldId == LocalWorldId ? Owner.Transform.WorldId : unchecked((byte)wireWorldId);
    }

    private Blocked FormatBlocked(uint characterId, string name, uint worldId)
    {
        return new Blocked
        {
            CharacterId = characterId,
            Name = name,
            WorldId = worldId == Owner.Transform.WorldId
                ? LocalWorldId
                : unchecked((sbyte)checked((byte)worldId))
        };
    }

    private void SendAddFailure(string name, sbyte worldId, ErrorMessageType error)
    {
        Owner.SendPacket(new SCAddBlockedUserPacket(
            new Blocked
            {
                Name = name,
                WorldId = worldId
            },
            false,
            error));
    }
}

public class BlockedTemplate
{
    public uint Owner { get; set; }
    public uint BlockedId { get; set; }
}
