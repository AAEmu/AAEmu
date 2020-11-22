using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncExitIndun : DoodadFuncTemplate
    {
        public uint ReturnPointId { get; set; }

        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            _log.Debug("DoodadFuncExitIndun, ReturnPointId: {0}", ReturnPointId);

            if (caster is Character character)
            {
                if (ReturnPointId == 0 && character.WorldPosition != null)
                {
                    character.DisabledSetPosition = true;

                    character.SendPacket(
                        new SCLoadInstancePacket(
                            1,
                            character.WorldPosition.ZoneId,
                            character.WorldPosition.X,
                            character.WorldPosition.Y,
                            character.WorldPosition.Z,
                            0,
                            0,
                            0
                        )
                    );

                    character.InstanceId = 1; // TODO ....
                    character.Position = character.WorldPosition.Clone();
                    character.WorldPosition = null;
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
