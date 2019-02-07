using AAEmu.Game.Core.Packets.G2C;
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
                character.Position.ZoneId = ZoneId;
                character.Position.X = 3680.518f;
                character.Position.Y = 4572.221f;
                character.Position.Z = 156;
                character.SendPacket(new SCLoadInstancePacket(2, (int)ZoneId, 3680.518f, 4572.221f, 156, 0, 0, 0));
            }
        }
    }
}
