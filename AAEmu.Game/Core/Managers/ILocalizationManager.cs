namespace AAEmu.Game.Core.Managers;

public interface ILocalizationManager : ILoadable
{
    void AddTranslation(string tblName, string tblColumn, long index, string translationValue);
    string Get(string tblName, string tblColumn, long index, string fallbackValue = "");
    bool MatchItemName(long itemTemplateId, string keywords, bool exactMatch);
}
