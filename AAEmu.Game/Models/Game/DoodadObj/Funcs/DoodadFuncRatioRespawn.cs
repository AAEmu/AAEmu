using System;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncRatioRespawn : DoodadFuncTemplate
    {
        public int Ratio { get; set; }
        public uint SpawnDoodadId { get; set; }

        public override void Use(Unit caster, Doodad owner, uint skillId)
        {
            _log.Debug("DoodadFuncRatioRespawn : Ratio {0}, SpawnDoodadId {1}", Ratio, SpawnDoodadId);

            if (caster is Character character)
            {
                var (newX, newY) = MathUtil.AddDistanceToFront(1, character.Position.X, character.Position.Y, character.Position.RotationZ); //TODO расстояние вперед 1 м

                // NPC spawn
                var npcSpawner = new NpcSpawner();
                npcSpawner.Id = 0;
                npcSpawner.RespawnTime = 0; // не появляться после смерти
                npcSpawner.UnitId = 7503; // Rat Id = 7503, FactionId = 3
                npcSpawner.Position = character.Position.Clone();
                npcSpawner.Position.X = newX + Rand.Next(-2, 2);
                npcSpawner.Position.Y = newY + Rand.Next(-2, 2);
                npcSpawner.Position.Z = character.Position.Z;
                // for work need enable geodata
                //npcSpawner.Position.Z = WorldManager.Instance.GetHeight(npcSpawner.Position.ZoneId, npcSpawner.Position.X, npcSpawner.Position.Y);
                // looks in the direction of the character
                var angle = MathUtil.CalculateAngleFrom(npcSpawner.Position.X, npcSpawner.Position.Y, character.Position.X, character.Position.Y);
                var rotZ = MathUtil.ConvertDegreeToDirection(angle);
                npcSpawner.Position.RotationX = 0;
                npcSpawner.Position.RotationY = 0;
                npcSpawner.Position.RotationZ = rotZ;
                var npc = npcSpawner.Spawn(0);
                npc.Respawn = DateTime.MinValue; // не появляться после смерти


                var target = (BaseUnit)character;
                npc.CurrentTarget = target;
                npc.SetForceAttack(true);
                npc.IsAutoAttack = true;

                var combat = new Combat();
                combat.Execute(npc);
            }
        }
    }
}
