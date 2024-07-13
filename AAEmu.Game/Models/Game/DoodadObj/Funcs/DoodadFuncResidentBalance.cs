using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncResidentBalance : DoodadPhaseFuncTemplate
{
    public int NextPhase { get; set; }

    public override bool Use(BaseUnit caster, Doodad owner)
    {
        Logger.Debug($"DoodadFuncResidentBalance: {NextPhase}");

        if (caster is Character character)
        {
            return false; // продолжим выполнение
        }
        return true; // прерываем
    }
}
