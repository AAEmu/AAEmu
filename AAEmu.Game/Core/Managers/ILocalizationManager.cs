namespace AAEmu.Game.Core.Managers;

public interface ILocalizationManager
{
    void Load();
    void AddTranslation(string tblName, string tblColumn, long index, string translationValue);
    string Get(string tblName, string tblColumn, long index, string fallbackValue = "");
}
