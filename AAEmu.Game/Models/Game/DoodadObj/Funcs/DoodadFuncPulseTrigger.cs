using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncPulseTrigger : DoodadPhaseFuncTemplate
{
    public bool Flag { get; set; }
    public int NextPhase { get; set; }

    public static bool Halt { get; set; } = false;

    public override bool Use(BaseUnit caster, Doodad owner)
    {
        Logger.Debug($"DoodadFuncPulseTrigger Flag={Flag}, NextPhase={NextPhase}, Halt={Halt}");

        if (Flag && !Halt)
        {
            Halt = true; // запрещаем выполнение в цикле // we prohibit execution in a loop
            owner.OverridePhase = NextPhase;

            return true;
        }

        return false;
    }
}
