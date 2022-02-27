using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncEnterInstance : DoodadFuncTemplate
    {
        // doodad_funcs
        public uint ZoneId { get; set; }
        public uint ItemId { get; set; }

        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            _log.Trace("DoodadFuncEnterInstance, ZoneId: {0}, ItemId: {1}", ZoneId, ItemId);

            if (caster is Character character)
            {
                character.DisabledSetPosition = true;
                var zone = ZoneManager.Instance.GetZoneById(ZoneId);
                var world = WorldManager.Instance.GetWorldByZone(zone.ZoneKey);

                if (world.SpawnPosition != null)
                {
                    character.SendPacket(
                        new SCLoadInstancePacket(
                            world.Id,
                            world.SpawnPosition.ZoneId,
                            world.SpawnPosition.X,
                            world.SpawnPosition.Y,
                            world.SpawnPosition.Z,
                            0,
                            0,
                            0
                        )
                    );

                    character.MainWorldPosition = character.Transform.CloneDetached(character);
                    // TODO: use proper instance Id using a manager
                    character.Transform = new Transform(character, null, world.Id, world.SpawnPosition.ZoneId, world.Id,
                        world.SpawnPosition.X, world.SpawnPosition.Y, world.SpawnPosition.Z, 0);
                    character.InstanceId = world.Id; // TODO all instances now
                }
                else
                    _log.Warn("World #.{0}, not have default spawn position.", world.Id);
            }

        }
    }
}
