namespace AAEmu.Game.Models.Game.Skills.DynamicEffects;

internal class DynamicEffect
{
    public Dictionary<uint, SelectiveItem> selectiveItems;
    public Dictionary<uint, BlessUthstin> blessUthstins;

    public DynamicEffect()
    {
        selectiveItems = new Dictionary<uint, SelectiveItem>();
        blessUthstins = new Dictionary<uint, BlessUthstin>();
    }
}
