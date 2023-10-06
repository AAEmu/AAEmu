﻿using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncConsumeChangerItem : DoodadPhaseFuncTemplate
{
    public uint DoodadFuncConsumeChangerId { get; set; }
    public uint ItemId { get; set; }

    public override bool Use(BaseUnit caster, Doodad owner)
    {
        Logger.Trace("DoodadFuncConsumeChangerItem");
        return false;
    }
}
