using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Managers;

public interface IFriendManager : ILoadable
{
    void RequestFriend(Character requester, string targetName);
    void AcceptFriend(Character receiver, ulong requesterCharacterId);
    void CancelFriend(Character actor, bool isReceive, ulong counterpartCharacterId);
    void DeleteFriend(Character requester, string friendName);
    void SendStatusChange(Character unit, bool forOnline, bool boolean);
}
