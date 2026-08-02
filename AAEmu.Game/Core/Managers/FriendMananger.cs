using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.World.Transform;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Core.Managers;

public class FriendMananger : Singleton<FriendMananger>, IFriendManager
{
    // std::map is the adjacent receiver list and enum_error_messages exposes its corresponding cap.
    public const int MaxAcceptedFriends = 100;
    public const int MaxOutgoingRequests = 50;
    public const int MaxIncomingRequests = 50;

    public static readonly TimeSpan PendingRequestLifetime = TimeSpan.FromDays(14);

    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private readonly object _relationshipLock = new();
    private Dictionary<(uint OwnerId, uint FriendId), FriendTemplate> _allFriends = [];

    public void Load()
    {
        lock (_relationshipLock)
        {
            _allFriends = [];

            Logger.Info("Loading friends ...");
            using var connection = MySQL.CreateConnection();
            using (var expireCommand = connection.CreateCommand())
            {
                expireCommand.CommandText =
                    "DELETE FROM friends WHERE status IN (@outgoing, @incoming) AND created_at < @expires_before";
                expireCommand.Parameters.AddWithValue("@outgoing", (byte)FriendStatus.OutgoingRequest);
                expireCommand.Parameters.AddWithValue("@incoming", (byte)FriendStatus.IncomingRequest);
                expireCommand.Parameters.AddWithValue("@expires_before", DateTime.UtcNow - PendingRequestLifetime);
                var expiredCount = expireCommand.ExecuteNonQuery();
                if (expiredCount > 0)
                    Logger.Info("Expired {0} stale friend request records", expiredCount);
            }

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM friends";
            command.Prepare();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var template = new FriendTemplate
                {
                    Id = reader.GetUInt32("id"),
                    FriendId = reader.GetUInt32("friend_id"),
                    Owner = reader.GetUInt32("owner"),
                    Status = (FriendStatus)reader.GetByte("status"),
                    CreatedAt = reader.GetDateTime("created_at")
                };
                _allFriends[(template.Owner, template.FriendId)] = template;
            }

            Logger.Info("Loaded {0} friend relationships", _allFriends.Count);
        }
    }

    public void RequestFriend(Character requester, string targetName)
    {
        targetName = targetName?.Trim();
        if (string.IsNullOrEmpty(targetName))
        {
            SendRequestFailure(requester, targetName ?? string.Empty, ErrorMessageType.FriendRequestInfoInvalid);
            return;
        }

        var targetInfo = GetFriendInfo(targetName);
        if (targetInfo == null)
        {
            SendRequestFailure(requester, targetName, ErrorMessageType.UserNotExist);
            return;
        }

        if (targetInfo.CharacterId == requester.Id)
        {
            SendRequestFailure(requester, targetName, ErrorMessageType.CannotAddFriendSelf, targetInfo);
            return;
        }

        var target = WorldManager.Instance.GetCharacterById(targetInfo.CharacterId);
        var requestedAt = DateTime.UtcNow;
        FriendTemplate outgoing;
        FriendTemplate incoming;

        lock (_relationshipLock)
        {
            if (_allFriends.TryGetValue((requester.Id, targetInfo.CharacterId), out var existing))
            {
                var error = existing.Status == FriendStatus.Accepted
                    ? ErrorMessageType.AlreadyFriend
                    : ErrorMessageType.FriendRequestExists;
                SendRequestFailure(requester, targetName, error, targetInfo);
                return;
            }

            if (_allFriends.TryGetValue((targetInfo.CharacterId, requester.Id), out var reverseExisting))
            {
                var error = reverseExisting.Status == FriendStatus.Accepted
                    ? ErrorMessageType.AlreadyFriend
                    : ErrorMessageType.FriendRequestExists;
                SendRequestFailure(requester, targetName, error, targetInfo);
                return;
            }

            if (CountRelationships(requester.Id, FriendStatus.Accepted) >= MaxAcceptedFriends ||
                CountRelationships(targetInfo.CharacterId, FriendStatus.Accepted) >= MaxAcceptedFriends)
            {
                SendRequestFailure(requester, targetName, ErrorMessageType.FriendListMax, targetInfo);
                return;
            }

            if (CountRelationships(requester.Id, FriendStatus.OutgoingRequest) >= MaxOutgoingRequests)
            {
                SendRequestFailure(requester, targetName, ErrorMessageType.FriendRequestListMax, targetInfo);
                return;
            }

            if (CountRelationships(targetInfo.CharacterId, FriendStatus.IncomingRequest) >= MaxIncomingRequests)
            {
                SendRequestFailure(requester, targetName, ErrorMessageType.FriendReceiverListMax, targetInfo);
                return;
            }

            if (IsBlocked(requester, targetInfo.CharacterId, target))
            {
                SendRequestFailure(requester, targetName, ErrorMessageType.FriendRequestImpossible, targetInfo);
                return;
            }

            outgoing = new FriendTemplate
            {
                Id = FriendIdManager.Instance.GetNextId(),
                FriendId = targetInfo.CharacterId,
                Owner = requester.Id,
                Status = FriendStatus.OutgoingRequest,
                CreatedAt = requestedAt
            };
            incoming = new FriendTemplate
            {
                Id = FriendIdManager.Instance.GetNextId(),
                FriendId = requester.Id,
                Owner = targetInfo.CharacterId,
                Status = FriendStatus.IncomingRequest,
                CreatedAt = requestedAt
            };

            try
            {
                InsertRequestPair(outgoing, incoming);
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Failed to persist friend request from {0} to {1}", requester.Id,
                    targetInfo.CharacterId);
                SendRequestFailure(requester, targetName, ErrorMessageType.FriendRequestFailed, targetInfo);
                return;
            }

            TrackRelationship(outgoing);
            TrackRelationship(incoming);
        }

        requester.SendPacket(new SCFriendRequestPacket(
            true,
            true,
            ErrorMessageType.NoErrorMessage,
            targetInfo.CharacterId,
            targetInfo.WorldId,
            requestedAt,
            targetInfo.Name));

        target?.SendPacket(new SCFriendRequestPacket(
            false,
            true,
            ErrorMessageType.NoErrorMessage,
            requester.Id,
            requester.Transform.WorldId,
            requestedAt,
            requester.Name));
    }

    public void AcceptFriend(Character receiver, ulong requesterCharacterId)
    {
        if (requesterCharacterId > uint.MaxValue)
        {
            SendAcceptFailure(receiver, requesterCharacterId, ErrorMessageType.FriendAcceptInfoInvalid);
            return;
        }

        var requesterId = (uint)requesterCharacterId;
        var requesterInfo = GetFriendInfo([requesterId]).SingleOrDefault();
        var requester = WorldManager.Instance.GetCharacterById(requesterId);
        var acceptedAt = DateTime.UtcNow;

        lock (_relationshipLock)
        {
            if (!_allFriends.TryGetValue((receiver.Id, requesterId), out var incoming) ||
                incoming.Status != FriendStatus.IncomingRequest ||
                !_allFriends.TryGetValue((requesterId, receiver.Id), out var outgoing) ||
                outgoing.Status != FriendStatus.OutgoingRequest)
            {
                SendAcceptFailure(receiver, requesterCharacterId, ErrorMessageType.FriendAcceptInfoInvalid,
                    requesterInfo);
                return;
            }

            if (incoming.CreatedAt < acceptedAt - PendingRequestLifetime ||
                outgoing.CreatedAt < acceptedAt - PendingRequestLifetime)
            {
                RemovePendingPair(incoming, outgoing);
                SendAcceptFailure(receiver, requesterCharacterId, ErrorMessageType.FriendAcceptInfoInvalid,
                    requesterInfo);
                return;
            }

            if (requesterInfo == null)
            {
                RemovePendingPair(incoming, outgoing);
                SendAcceptFailure(receiver, requesterCharacterId, ErrorMessageType.UserNotExist);
                return;
            }

            if (CountRelationships(receiver.Id, FriendStatus.Accepted) >= MaxAcceptedFriends ||
                CountRelationships(requesterId, FriendStatus.Accepted) >= MaxAcceptedFriends)
            {
                SendAcceptFailure(receiver, requesterCharacterId, ErrorMessageType.FriendListMax, requesterInfo);
                return;
            }

            if (IsBlocked(receiver, requesterId, requester))
            {
                SendAcceptFailure(receiver, requesterCharacterId, ErrorMessageType.FriendRequestImpossible,
                    requesterInfo);
                return;
            }

            try
            {
                UpdateAcceptedPair(receiver.Id, requesterId, acceptedAt);
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Failed to accept friend request between {0} and {1}", receiver.Id,
                    requesterId);
                SendAcceptFailure(receiver, requesterCharacterId, ErrorMessageType.FriendAcceptFailed,
                    requesterInfo);
                return;
            }

            incoming.Status = FriendStatus.Accepted;
            incoming.CreatedAt = acceptedAt;
            outgoing.Status = FriendStatus.Accepted;
            outgoing.CreatedAt = acceptedAt;
            TrackRelationship(incoming);
            TrackRelationship(outgoing);
        }

        ApplyRelationship(requesterInfo, FriendStatus.Accepted, acceptedAt);
        receiver.SendPacket(new SCFriendAcceptPacket(
            true, true, ErrorMessageType.NoErrorMessage, requesterInfo));

        if (requester != null)
        {
            var receiverInfo = FormatFriend(receiver);
            ApplyRelationship(receiverInfo, FriendStatus.Accepted, acceptedAt);
            requester.SendPacket(new SCFriendAcceptPacket(
                true, false, ErrorMessageType.NoErrorMessage, receiverInfo));
        }
    }

    public void CancelFriend(Character actor, bool isReceive, ulong counterpartCharacterId)
    {
        if (counterpartCharacterId > uint.MaxValue)
        {
            actor.SendPacket(new SCFriendCancelPacket(false, isReceive, actor.Id, counterpartCharacterId));
            return;
        }

        var counterpartId = (uint)counterpartCharacterId;
        var actorStatus = isReceive ? FriendStatus.IncomingRequest : FriendStatus.OutgoingRequest;
        var counterpartStatus = isReceive ? FriendStatus.OutgoingRequest : FriendStatus.IncomingRequest;
        var counterpart = WorldManager.Instance.GetCharacterById(counterpartId);

        lock (_relationshipLock)
        {
            if (!_allFriends.TryGetValue((actor.Id, counterpartId), out var actorRelationship) ||
                actorRelationship.Status != actorStatus ||
                !_allFriends.TryGetValue((counterpartId, actor.Id), out var counterpartRelationship) ||
                counterpartRelationship.Status != counterpartStatus)
            {
                actor.SendPacket(new SCFriendCancelPacket(false, isReceive, actor.Id, counterpartCharacterId));
                return;
            }

            try
            {
                DeletePendingPair(actorRelationship, counterpartRelationship);
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Failed to cancel friend request between {0} and {1}", actor.Id,
                    counterpartId);
                actor.SendPacket(new SCFriendCancelPacket(false, isReceive, actor.Id, counterpartCharacterId));
                return;
            }

            UntrackRelationship(actorRelationship);
            UntrackRelationship(counterpartRelationship);
        }

        actor.SendPacket(new SCFriendCancelPacket(true, isReceive, actor.Id, counterpartCharacterId));
        counterpart?.SendPacket(new SCFriendCancelPacket(true, !isReceive, counterpartId, actor.Id));
    }

    public void DeleteFriend(Character requester, string friendName)
    {
        friendName = friendName?.Trim();
        var friendInfo = string.IsNullOrEmpty(friendName) ? null : GetFriendInfo(friendName);
        if (friendInfo == null)
        {
            requester.SendPacket(new SCDeleteFriendPacket(
                true,
                0,
                friendName ?? string.Empty,
                false,
                ErrorMessageType.CannotFindInFriendList));
            return;
        }

        var friend = WorldManager.Instance.GetCharacterById(friendInfo.CharacterId);
        FriendTemplate requesterRelationship;
        FriendTemplate friendRelationship = null;

        lock (_relationshipLock)
        {
            if (!_allFriends.TryGetValue((requester.Id, friendInfo.CharacterId), out requesterRelationship) ||
                requesterRelationship.Status != FriendStatus.Accepted)
            {
                requester.SendPacket(new SCDeleteFriendPacket(
                    true,
                    friendInfo.CharacterId,
                    friendInfo.Name,
                    false,
                    ErrorMessageType.CannotFindInFriendList));
                return;
            }

            _allFriends.TryGetValue((friendInfo.CharacterId, requester.Id), out friendRelationship);
            try
            {
                DeleteAcceptedRelationships(requester.Id, friendInfo.CharacterId);
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Failed to delete friendship between {0} and {1}", requester.Id,
                    friendInfo.CharacterId);
                requester.SendPacket(new SCDeleteFriendPacket(
                    true,
                    friendInfo.CharacterId,
                    friendInfo.Name,
                    false,
                    ErrorMessageType.DeleteFriend));
                return;
            }

            UntrackRelationship(requesterRelationship);
            if (friendRelationship?.Status == FriendStatus.Accepted)
                UntrackRelationship(friendRelationship);
        }

        requester.SendPacket(new SCDeleteFriendPacket(
            true,
            friendInfo.CharacterId,
            friendInfo.Name,
            true,
            ErrorMessageType.NoErrorMessage));
        friend?.SendPacket(new SCDeleteFriendPacket(
            false,
            requester.Id,
            requester.Name,
            true,
            ErrorMessageType.NoErrorMessage));
    }

    public void SendStatusChange(Character unit, bool forOnline, bool boolean)
    {
        FriendTemplate[] relationships;
        lock (_relationshipLock)
            relationships = _allFriends.Values.Where(value => value.FriendId == unit.Id).ToArray();

        foreach (var relationship in relationships)
        {
            var friendOwner = WorldManager.Instance.GetCharacterById(relationship.Owner);
            if (friendOwner == null)
                continue;

            var friend = FormatFriend(unit);
            ApplyRelationship(friend, relationship.Status, relationship.CreatedAt);
            if (forOnline)
                friend.IsOnline = boolean;
            else
                friend.InParty = boolean;
            friendOwner.SendPacket(new SCFriendStatusChangedPacket(false, friend));
        }
    }

    public static List<Friend> GetFriendInfo(List<uint> ids)
    {
        var friendsList = new List<Friend>();
        var offlineIds = new List<uint>();
        foreach (var id in ids)
        {
            var friend = WorldManager.Instance.GetCharacterById(id);
            if (friend == null)
            {
                offlineIds.Add(id);
                continue;
            }

            friendsList.Add(FormatFriend(friend));
        }

        if (offlineIds.Count <= 0)
            return friendsList;

        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM characters WHERE id IN(" + string.Join(",", offlineIds) + ")";
        command.Prepare();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            friendsList.Add(new Friend
            {
                Name = reader.GetString("name"),
                CharacterId = reader.GetUInt32("id"),
                Position = new Transform(null, null,
                    reader.GetUInt32("zone_id"),
                    WorldManager.DefaultInstanceId,
                    reader.GetFloat("x"), reader.GetFloat("y"), reader.GetFloat("z"),
                    0, 0, 0),
                InParty = false,
                IsOnline = false,
                Race = (Race)reader.GetUInt32("race"),
                Level = reader.GetByte("level"),
                HeirLevel = HeirGameData.Instance.GetLevelForExp(reader.GetInt64("heir_exp")),
                LastWorldLeaveTime = reader.GetDateTime("leave_time"),
                Health = reader.GetInt32("hp"),
                Ability1 = (AbilityType)reader.GetByte("ability1"),
                Ability2 = (AbilityType)reader.GetByte("ability2"),
                Ability3 = (AbilityType)reader.GetByte("ability3"),
                WorldId = reader.GetUInt32("world_id"),
                RequestWorldId = reader.GetUInt32("world_id")
            });
        }

        return friendsList;
    }

    public static Friend GetFriendInfo(string name)
    {
        var friend = WorldManager.Instance.GetCharacter(name);
        if (friend != null)
            return FormatFriend(friend);

        uint? friendId = null;
        using (var connection = MySQL.CreateConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id FROM characters WHERE `name` = @name LIMIT 1";
            command.Parameters.AddWithValue("@name", name);
            command.Prepare();
            using var reader = command.ExecuteReader();
            if (reader.Read())
                friendId = reader.GetUInt32("id");
        }

        return friendId.HasValue ? GetFriendInfo([friendId.Value]).SingleOrDefault() : null;
    }

    private int CountRelationships(uint ownerId, FriendStatus status)
    {
        return _allFriends.Count(pair => pair.Key.OwnerId == ownerId && pair.Value.Status == status);
    }

    private static bool IsBlocked(Character first, uint secondId, Character second)
    {
        if (first.Blocked?.Contains(secondId) == true ||
            second?.Blocked?.Contains(first.Id) == true)
            return true;

        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT 1 FROM blocked WHERE (owner = @first AND blocked_id = @second) " +
            "OR (owner = @second AND blocked_id = @first) LIMIT 1";
        command.Parameters.AddWithValue("@first", first.Id);
        command.Parameters.AddWithValue("@second", secondId);
        return command.ExecuteScalar() != null;
    }

    private static void InsertRequestPair(FriendTemplate outgoing, FriendTemplate incoming)
    {
        using var connection = MySQL.CreateConnection();
        using var transaction = connection.BeginTransaction();
        InsertRelationship(connection, transaction, outgoing);
        InsertRelationship(connection, transaction, incoming);
        transaction.Commit();
    }

    private static void InsertRelationship(
        MySqlConnection connection,
        MySqlTransaction transaction,
        FriendTemplate relationship)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO friends(`id`,`friend_id`,`owner`,`status`,`created_at`) " +
            "VALUES (@id, @friend_id, @owner, @status, @created_at)";
        command.Parameters.AddWithValue("@id", relationship.Id);
        command.Parameters.AddWithValue("@friend_id", relationship.FriendId);
        command.Parameters.AddWithValue("@owner", relationship.Owner);
        command.Parameters.AddWithValue("@status", (byte)relationship.Status);
        command.Parameters.AddWithValue("@created_at", relationship.CreatedAt);
        if (command.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("The friend relationship insert did not affect exactly one row.");
    }

    private static void UpdateAcceptedPair(uint receiverId, uint requesterId, DateTime acceptedAt)
    {
        using var connection = MySQL.CreateConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "UPDATE friends SET status = @accepted, created_at = @accepted_at WHERE " +
            "(owner = @receiver AND friend_id = @requester AND status = @incoming) OR " +
            "(owner = @requester AND friend_id = @receiver AND status = @outgoing)";
        command.Parameters.AddWithValue("@accepted", (byte)FriendStatus.Accepted);
        command.Parameters.AddWithValue("@accepted_at", acceptedAt);
        command.Parameters.AddWithValue("@receiver", receiverId);
        command.Parameters.AddWithValue("@requester", requesterId);
        command.Parameters.AddWithValue("@incoming", (byte)FriendStatus.IncomingRequest);
        command.Parameters.AddWithValue("@outgoing", (byte)FriendStatus.OutgoingRequest);
        if (command.ExecuteNonQuery() != 2)
            throw new InvalidOperationException("The friend acceptance did not update both relationship rows.");
        transaction.Commit();
    }

    private void RemovePendingPair(FriendTemplate first, FriendTemplate second)
    {
        try
        {
            DeletePendingPair(first, second);
            UntrackRelationship(first);
            UntrackRelationship(second);
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Failed to remove expired friend request between {0} and {1}", first.Owner,
                first.FriendId);
        }
    }

    private static void DeletePendingPair(FriendTemplate first, FriendTemplate second)
    {
        using var connection = MySQL.CreateConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "DELETE FROM friends WHERE " +
            "(owner = @first_owner AND friend_id = @first_friend AND status = @first_status) OR " +
            "(owner = @second_owner AND friend_id = @second_friend AND status = @second_status)";
        command.Parameters.AddWithValue("@first_owner", first.Owner);
        command.Parameters.AddWithValue("@first_friend", first.FriendId);
        command.Parameters.AddWithValue("@first_status", (byte)first.Status);
        command.Parameters.AddWithValue("@second_owner", second.Owner);
        command.Parameters.AddWithValue("@second_friend", second.FriendId);
        command.Parameters.AddWithValue("@second_status", (byte)second.Status);
        if (command.ExecuteNonQuery() != 2)
            throw new InvalidOperationException("The friend request cancellation did not delete both rows.");
        transaction.Commit();
    }

    private static void DeleteAcceptedRelationships(uint requesterId, uint friendId)
    {
        using var connection = MySQL.CreateConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "DELETE FROM friends WHERE status = @accepted AND " +
            "((owner = @requester AND friend_id = @friend) OR " +
            "(owner = @friend AND friend_id = @requester))";
        command.Parameters.AddWithValue("@accepted", (byte)FriendStatus.Accepted);
        command.Parameters.AddWithValue("@requester", requesterId);
        command.Parameters.AddWithValue("@friend", friendId);
        if (command.ExecuteNonQuery() < 1)
            throw new InvalidOperationException("The friendship deletion did not delete a relationship row.");
        transaction.Commit();
    }

    private void TrackRelationship(FriendTemplate relationship)
    {
        _allFriends[(relationship.Owner, relationship.FriendId)] = relationship;
        WorldManager.Instance.GetCharacterById(relationship.Owner)?.Friends?.SetRelationship(relationship);
    }

    private void UntrackRelationship(FriendTemplate relationship)
    {
        _allFriends.Remove((relationship.Owner, relationship.FriendId));
        WorldManager.Instance.GetCharacterById(relationship.Owner)?.Friends?.RemoveRelationship(relationship.FriendId);
    }

    private static void ApplyRelationship(Friend friend, FriendStatus status, DateTime createdAt)
    {
        friend.Status = status;
        friend.FriendCreateTime = createdAt;
    }

    private static void SendRequestFailure(
        Character requester,
        string targetName,
        ErrorMessageType error,
        Friend target = null)
    {
        requester.SendPacket(new SCFriendRequestPacket(
            true,
            false,
            error,
            target?.CharacterId ?? 0,
            target?.WorldId ?? 0,
            DateTime.MinValue,
            target?.Name ?? targetName));
    }

    private static void SendAcceptFailure(
        Character receiver,
        ulong requesterCharacterId,
        ErrorMessageType error,
        Friend requester = null)
    {
        requester ??= new Friend
        {
            CharacterId = requesterCharacterId <= uint.MaxValue ? (uint)requesterCharacterId : 0,
            Name = string.Empty,
            LastWorldLeaveTime = DateTime.MinValue,
            FriendCreateTime = DateTime.MinValue
        };
        receiver.SendPacket(new SCFriendAcceptPacket(false, true, error, requester));
    }

    private static Friend FormatFriend(Character friend)
    {
        return new Friend
        {
            Name = friend.Name,
            CharacterId = friend.Id,
            Position = friend.Transform.Clone(),
            InParty = friend.InParty,
            IsOnline = true,
            Race = friend.Race,
            Level = friend.Level,
            HeirLevel = friend.HeirLevel,
            LastWorldLeaveTime = friend.LeaveTime,
            Health = friend.Hp,
            Ability1 = friend.Ability1,
            Ability2 = friend.Ability2,
            Ability3 = friend.Ability3,
            WorldId = friend.Transform.WorldId,
            RequestWorldId = friend.Transform.WorldId
        };
    }
}

public class FriendTemplate
{
    public uint Id { get; set; }
    public uint FriendId { get; set; }
    public uint Owner { get; set; }
    public FriendStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
}
