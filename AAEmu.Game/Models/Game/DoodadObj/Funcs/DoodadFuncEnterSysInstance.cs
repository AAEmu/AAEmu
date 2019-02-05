using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncEnterSysInstance : DoodadFuncTemplate
    {
        public uint ZoneId { get; set; }
        public uint FactionId { get; set; }

        public override void Use(Unit caster, Doodad owner, uint skillId)
        {
            _log.Debug("DoodadFuncEnterSysInstance");

            // TODO Debug
            if (caster is Character character)
            {
                character.SendMessage("Coming soon!");
                // character.SendPacket(new SCLoadInstancePacket(2, 183, 3680.518f - 14336, 4572.221f - 3072, 156, 0, 0, 0));
            }
        }
    }
}
