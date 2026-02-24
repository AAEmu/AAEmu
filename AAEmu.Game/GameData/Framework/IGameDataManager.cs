using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.GameData.Framework;

public interface IGameDataManager : ILoadable
{
    void LoadGameData();
    void PostLoadGameData();
}