using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Managers;

public interface IFamilyManager : ILoadable
{
    void SaveAllFamilies();
    void OnCharacterLogin(Character character);
    void OnCharacterLogout(Character character);
    void LeaveFamily(Character character);
}
