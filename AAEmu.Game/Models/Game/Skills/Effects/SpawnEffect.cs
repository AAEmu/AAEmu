using System;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills.Effects.Enums;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class SpawnEffect : EffectTemplate
{
    public BaseUnitType OwnerTypeId { get; set; }
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
    public MateState MateStateId { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        Logger.Info($"SpawnEffect: OwnerTypeId={OwnerTypeId}, SubType={SubType}, UseSummonerFaction={UseSummonerFaction}, LifeTime={LifeTime}");

        var random = new Random();
        var PosAngle = (float)(PosAngleMin + (PosAngleMax - PosAngleMin) * random.NextDouble());
        float PosDistance;
        if (PosDistanceMin != 0 && PosDistanceMax != 0)
        {
            PosDistance = (float)(PosDistanceMin + (PosDistanceMax - PosDistanceMin) * random.NextDouble());
        }
        else
        {
            PosDistanceMin = 2;
            PosDistanceMax = 3;
            PosDistance = (float)(PosDistanceMin + (PosDistanceMax - PosDistanceMin) * random.NextDouble());
        }

        // dir id 1 = relative to target/spawner.
        // dir id 2 = relative to caster.
        var positionRelativeToUnit = PosDirId switch
        {
            1 => target,
            2 => caster,
            _ => caster
        };

        var orientationRelativeToUnit = OriDirId switch
        {
            1 => target,
            2 => caster,
            _ => caster
        };

        if (positionRelativeToUnit == null || orientationRelativeToUnit == null)
        {
            Logger.Warn($"SpawnEffect: Unhandled PosDirId {PosDirId} or OriDirId {OriDirId}");
            return;
        }

        switch (OwnerTypeId)
        {
            case BaseUnitType.Npc:
                {
                    var spawner = SpawnManager.Instance.GetNpcSpawner(SubType, target);
                    if (spawner == null)
                    {
                        Logger.Info($"SpawnEffect: SubType={SubType} not found in spawners.");
                        return;
                    }

                    var (xx, yy) = MathUtil.AddDistanceToFrontDeg(PosDistance, positionRelativeToUnit.Transform.World.Position.X, positionRelativeToUnit.Transform.World.Position.Y, PosAngle);

                    // TODO: Not sure if this is needed.
                    //var zz = WorldManager.Instance.GetHeight(target.Transform.ZoneId, xx, yy);
                    //if (zz == 0) {
                    //	zz = target.Transform.World.Position.Z;
                    //}

                    spawner.Position.X = xx;
                    spawner.Position.Y = yy;
                    spawner.Position.Z = positionRelativeToUnit.Transform.World.Position.Z;

                    spawner.Position.Yaw = orientationRelativeToUnit.Transform.World.Rotation.Z + PosAngle.DegToRad();

                    spawner.RespawnTime = 0; // don't respawn

                    spawner.DoSpawnEffect(spawner.Id, this, caster, target);
                    break;
                }
            case BaseUnitType.Slave:
                {
                    if (caster is Character player)
                    {
                        // TODO: Implement OriDirId, PosDirId and MateStateId
                        using var transform = positionRelativeToUnit.Transform.CloneDetached();
                        transform.World.AddDistanceToFront(PosDistance);
                        transform.World.Rotate(transform.World.Rotation with { Z = orientationRelativeToUnit.Transform.World.Rotation.Z + OriAngle.DegToRad() });

                        var slave = SlaveManager.Instance.Create(SubType, false, transform);
                        if (slave is { Template: null })
                        {
                            Logger.Info($"SpawnEffect: SubType={SubType} not found...");
                            return;
                        }
                        player.ForceDismountAndDespawn(slave, 500000); // delete Slave after 8min 20s
                    }
                    break;
                }
            case BaseUnitType.Mate:
                break;
            case BaseUnitType.Character:
                break;
            case BaseUnitType.Housing:
                break;
            case BaseUnitType.Transfer:
                break;
            case BaseUnitType.Shipyard:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}
