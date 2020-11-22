using System;
using System.Runtime.CompilerServices;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Doodads;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLog.LayoutRenderers;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncTod : DoodadFuncTemplate
    {
        public int Tod { get; set; }
        public uint NextPhase { get; set; }
        
        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            if (caster is Character character)
            {
                //I think this is used to reschedule anything that needs triggered at a specific gametime

            }
        }
    }
}
