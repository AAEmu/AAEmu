using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncExitIndun : DoodadFuncTemplate
    {
        // doodad_funcs
        public uint ReturnPointId { get; set; }

        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            _log.Trace("DoodadFuncExitIndun, ReturnPointId: {0}", ReturnPointId);

            if (caster is Character character)
            {
                if (ReturnPointId == 0 && character.MainWorldPosition != null)
                {
                    character.DisabledSetPosition = true;

                    character.SendPacket(
                        new SCLoadInstancePacket(
                            1,
                            character.MainWorldPosition.ZoneId,
                            character.MainWorldPosition.World.Position.X,
                            character.MainWorldPosition.World.Position.Y,
                            character.MainWorldPosition.World.Position.Z,
                            character.MainWorldPosition.World.Rotation.X,
                            character.MainWorldPosition.World.Rotation.Y,
                            character.MainWorldPosition.World.Rotation.Z
                        )
                    );

                    character.Transform = character.MainWorldPosition.Clone(character);
                    character.MainWorldPosition = null;
                }
                else
                {
                    // TODO in db not have a entries, but we can change this xD
                    _log.Warn("DoodadFuncExitIndun, Not have return point!");
                }
            }
        }
    }
}
