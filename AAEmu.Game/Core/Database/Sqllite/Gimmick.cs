using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class Gimmick
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public string ModelPath { get; set; }

    public double? Mass { get; set; }

    public double? Density { get; set; }

    public byte[] PushableByPlayer { get; set; }

    public double? WaterDamping { get; set; }

    public double? WaterDensity { get; set; }

    public double? WaterResistance { get; set; }

    public long? SkillId { get; set; }

    public long? SkillDelay { get; set; }

    public long? LifeTime { get; set; }

    public double? CollisionMinSpeed { get; set; }

    public long? CollisionSkillId { get; set; }

    public byte[] DisappearByCollision { get; set; }

    public byte[] CollisionUnitOnly { get; set; }

    public long? SpawnDelay { get; set; }

    public long? FadeInDuration { get; set; }

    public long? FadeOutDuration { get; set; }

    public double? Damping { get; set; }

    public double? FreeFallDamping { get; set; }

    public byte[] Graspable { get; set; }

    public double? Gravity { get; set; }

    public double? AirResistance { get; set; }

    public byte[] NoGroundCollider { get; set; }
}
