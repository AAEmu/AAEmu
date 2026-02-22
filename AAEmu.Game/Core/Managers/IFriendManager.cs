using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Managers;

public interface IFriendManager : ILoadable
{
    void AddToAllFriends(FriendTemplate template);
    void RemoveFromAllFriends(uint id);
    void SendStatusChange(Character unit, bool forOnline, bool boolean);
}
