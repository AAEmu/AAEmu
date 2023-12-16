using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Teleport;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncEnterSysInstance : DoodadFuncTemplate
    {
        // doodad_funcs
        public uint ZoneId { get; set; }
        public uint FactionId { get; set; }

        public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            Logger.Info("DoodadFuncEnterSysInstance, ZoneId: {0}", ZoneId);
            if (caster is Character character)
            {
                character.DisabledSetPosition = true;
                var zone = ZoneManager.Instance.GetZoneById(ZoneId);
                var world = WorldManager.Instance.GetWorldByZone(zone.ZoneKey);
                // we take the coordinates of the zone
                foreach (var wz in world.XmlWorldZones.Values)
                {
                    if (wz.Id == zone.ZoneKey)
                    {
                        world.SpawnPosition = wz.SpawnPosition;
                    }
                }
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
                            world.SpawnPosition.Yaw.DegToRad()
                        )
                    );

                    character.MainWorldPosition = character.Transform.CloneDetached(character);
                    // TODO: use proper instance Id using a manager
                    //character.Transform = new Transform(character, null, world.Id, world.SpawnPosition.ZoneId, world.Id, world.SpawnPosition.X, world.SpawnPosition.Y, world.SpawnPosition.Z, world.SpawnPosition.Yaw.DegToRad());
                    character.Transform.ApplyWorldSpawnPosition(world.SpawnPosition, world.Id);
                    character.InstanceId = world.Id; // TODO all instances are sys now

                    character.DisabledSetPosition = true;
                    character.SendPacket(new SCTeleportUnitPacket(TeleportReason.IndunDirectTel, 0, world.SpawnPosition.X, world.SpawnPosition.Y, world.SpawnPosition.Z, world.SpawnPosition.Yaw.DegToRad()));
                }
                else
                    Logger.Info("World {0} (#.{1}), does not have a default spawn position.", world.Name, world.Id);
            }

        }
    }
}
