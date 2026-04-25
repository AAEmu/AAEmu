using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Gimmicks;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class Projectile : SpecialEffectAction
{
    public override void Execute(BaseUnit caster,
        SkillCaster casterObj,
        BaseUnit target,
        SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill,
        SkillObject skillObject,
        DateTime time,
        int value1,
        int value2,
        int value3,
        int value4)
    {
        if (caster is Character)
            Logger.Debug("Special effects: Projectile value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4);

        if (caster is not Unit casterUnit)
            return;

        if (casterUnit.ParentWorld?.GimmickManager == null)
            return;

        var templateId = (uint)Math.Max(0, value1);
        if (templateId == 0)
            return;

        // Replace existing stored projectile (some skills reuse the same slot).
        if (casterUnit.Gimmick != null)
        {
            casterUnit.Gimmick.Spawner?.Despawn(casterUnit.Gimmick);
            casterUnit.Gimmick = null;
        }

        var gimmick = casterUnit.ParentWorld.GimmickManager.Create(templateId);
        if (gimmick == null)
            return;

        gimmick.Spawner ??= new GimmickSpawner(casterUnit.ParentWorld) { RespawnTime = 0 };
        gimmick.Spawner.ParentWorld = casterUnit.ParentWorld;
        gimmick.Transform = casterUnit.Transform.CloneDetached(gimmick);
        gimmick.SpawnerUnitId = casterUnit.ObjId;
        gimmick.GrasperUnitId = 0;

        // Spawn slightly in front to avoid immediate ground intersections.
        gimmick.Transform.Local.AddDistanceToFront(1f);

        var speed = Math.Max(0f, value2);
        if (speed > 0.0001f)
        {
            var yaw = gimmick.Transform.World.Rotation.Z; // radians
            gimmick.Vel = new System.Numerics.Vector3(MathF.Cos(yaw) * speed, MathF.Sin(yaw) * speed, 0f);
        }

        gimmick.Spawn();
        casterUnit.ParentWorld.GimmickManager.AddActiveGimmick(gimmick);
        casterUnit.Gimmick = gimmick;
    }
}
