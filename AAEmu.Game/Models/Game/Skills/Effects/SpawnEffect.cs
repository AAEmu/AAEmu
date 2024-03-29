using System;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class SpawnEffect : EffectTemplate
{
    public uint OwnerTypeId { get; set; }
    public uint SubType { get; set; }
    public uint PosDirId { get; set; }
    //public float PosAngle { get; set; } // there is no such field in the database for version 3.0.3.0
    public float PosAngleMax { get; set; }
    public float PosAngleMin { get; set; }
    public float PosDistanceMax { get; set; }
    public float PosDistanceMin { get; set; }
    public uint OriDirId { get; set; }
    public float OriAngle { get; set; }
    public bool UseSummonerFaction { get; set; }
    public float LifeTime { get; set; }
    public bool DespawnOnCreatorDeath { get; set; }
    public bool UseSummonerAggroTarget { get; set; }
    public uint MateStateId { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        Logger.Info($"SpawnEffect: OwnerTypeId={OwnerTypeId}, SubType={SubType}, UseSummonerFaction={UseSummonerFaction}, LifeTime={LifeTime}");

        if (OwnerTypeId == 1) // NPC
        {
            var spawner = SpawnManager.Instance.GetNpcSpawner(SubType, target);
            if (spawner == null)
            {
                Logger.Info($"SpawnEffect: SubType={SubType} not found in spawners.");
                return;
            }
            var posAngle = Rand.Next(PosAngleMin, PosAngleMax);
            var posDistance = Rand.Next(PosDistanceMin, PosDistanceMax);
            var (xx, yy) = MathUtil.AddDistanceToFrontDeg(posDistance, target.Transform.World.Position.X, target.Transform.World.Position.Y, posAngle);
            var zz = WorldManager.Instance.GetHeight(target.Transform.ZoneId, xx, yy);
            if (zz == 0)
            {
                zz = target.Transform.World.Position.Z;
            }
            spawner.Position.X = xx;
            spawner.Position.Y = yy;
            spawner.Position.Z = zz;
            spawner.Position.Yaw = posAngle;

            spawner.RespawnTime = 0; // don't respawn

            spawner.DoSpawnEffect(spawner.Id, this, caster, target);
        }
    }
}
