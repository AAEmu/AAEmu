namespace AAEmu.Game.Core.Managers;

public interface IAccessLevelManager
{
    void Load();
    int GetLevel(string commandStr);
}
