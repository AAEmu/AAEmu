﻿using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncCraftStartCraft : DoodadPhaseFuncTemplate
{
    public uint DoodadFuncCraftStartId { get; set; }
    public uint CraftId { get; set; }

    public override bool Use(BaseUnit caster, Doodad owner)
    {
        Logger.Trace("DoodadFuncCraftStartCraft");
        return false;
    }
}
