using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Models.Game.Char;

public class CharacterFriends(Character owner)
{
    private readonly object _relationshipLock = new();

    public Character Owner { get; set; } = owner;
    public Dictionary<uint, FriendTemplate> FriendsIdList { get; set; } = []; // friendId, Template

    public void RemoveFriend(string name)
    {
        FriendMananger.Instance.DeleteFriend(Owner, name);
    }

    internal void SetRelationship(FriendTemplate relationship)
    {
        lock (_relationshipLock)
            FriendsIdList[relationship.FriendId] = relationship;
    }

    internal void RemoveRelationship(uint friendId)
    {
        lock (_relationshipLock)
            FriendsIdList.Remove(friendId);
    }

    public void Send()
    {
        // The client expects SCFriends at entry to initialize its friend list; the reference server sends an
        // empty one (total=0, count=0 -> 8-byte body) even with no friends. The packet already serializes 8 bytes
        // for an empty array, so send it unconditionally instead of skipping.
        Dictionary<uint, FriendTemplate> relationships;
        lock (_relationshipLock)
            relationships = new Dictionary<uint, FriendTemplate>(FriendsIdList);

        var allFriends = FriendMananger.GetFriendInfo([.. relationships.Keys]);
        foreach (var friend in allFriends)
        {
            if (!relationships.TryGetValue(friend.CharacterId, out var relationship))
                continue;
            friend.Status = relationship.Status;
            friend.FriendCreateTime = relationship.CreatedAt;
        }
        var allFriendsArray = new Friend[allFriends.Count];
        allFriends.CopyTo(allFriendsArray, 0);
        if (allFriendsArray.Length == 0)
        {
            Owner.SendPacket(new SCFriendsPacket(0, []));
            return;
        }

        foreach (var page in allFriendsArray.Chunk(SCFriendsPacket.MaxCountPerPacket))
            Owner.SendPacket(new SCFriendsPacket(allFriendsArray.Length, page));
    }

    public void Load(MySqlConnection connection)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM friends WHERE `owner` = @owner";
            command.Parameters.AddWithValue("@owner", Owner.Id);
            command.Prepare();
            using (var reader = command.ExecuteReader())
            {
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
                    lock (_relationshipLock)
                        FriendsIdList.Add(template.FriendId, template);
                }
            }
        }
    }

    public void Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        // FriendMananger commits every lifecycle transition atomically when it occurs. Rewriting a snapshot
        // during the general character save could resurrect a request concurrently canceled by its other party.
    }
}
