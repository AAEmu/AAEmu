using System;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Skills;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class SpawnEffect : EffectTemplate
    {
        public uint OwnerTypeId { get; set; }
        public uint SubType { get; set; }
        public uint PosDirId { get; set; }
        public float PosAngle { get; set; }
        public float PosDistance { get; set; }
        public uint OriDirId { get; set; }
        public float OriAngle { get; set; }
        public bool UseSummonerFaction { get; set; }
        public float LifeTime { get; set; }
        public bool DespawnOnCreatorDeath { get; set; }

        public bool UseSummoneerAggroTarget { get; set; }
        // TODO 1.2 // public uint MateStateId { get; set; }

        public override bool OnActionTime => false;

        public override void Apply(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj, CastAction castObj,
            EffectSource source, SkillObject skillObject, DateTime time, CompressedGamePackets packetBuilder = null)
        {
            _log.Trace("SpawnEffect");

            if (OwnerTypeId == 1) // NPC
            {
                var npc = NpcManager.Instance.Create(0, SubType);
                npc.Spawner = new NPChar.NpcSpawner();
                npc.Spawner.RespawnTime = 0; // don't respawn
                npc.Transform = caster.Transform.CloneDetached(npc);
                var rpy = target.Transform.World.ToRollPitchYawDegrees();
                var (xx, yy) = MathUtil.AddDistanceToFrontDeg(5f, npc.Transform.World.Position.X, npc.Transform.World.Position.Y, rpy.Z + 90f); //  + 90f to Front
                npc.SetPosition(xx, yy, target.Transform.World.Position.Z, rpy.X, rpy.Y, rpy.Z);
                if (AppConfiguration.Instance.HeightMapsEnable)
                    npc.Transform.Local.SetHeight(WorldManager.Instance.GetHeight(npc.Transform.ZoneId, xx, yy));

                if (npc.Ai != null)
                {
                    npc.Ai.IdlePosition = npc.Transform.CloneDetached();
                    npc.Ai.GoToSpawn();
                }

                if (UseSummonerFaction)
                    npc.Faction = caster.Faction;

                npc.Spawn();

                if (UseSummoneerAggroTarget)
                {
                    // TODO : Pick random target off of Aggro table ?

                    // Npc attacks the character
                    npc.Ai.Owner.AddUnitAggro(AggroKind.Damage, caster, 1);
                    npc.Ai.OnAggroTargetChanged();
                    npc.Ai.GoToCombat();
                }

                if (LifeTime > 0)
                {
                    var despawnTask = new DespawnTask(npc);
                    TaskManager.Instance.Schedule(despawnTask, TimeSpan.FromSeconds(LifeTime));
                }
            }
        }
    }
}
