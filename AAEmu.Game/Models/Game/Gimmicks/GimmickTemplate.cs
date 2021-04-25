namespace AAEmu.Game.Models.Game.Gimmicks
{
    public class GimmickTemplate
    {
        public uint Id { get; set; } // TemplateId
        public float AirResistance { get; set; }
        public float CollisionMinSpeed { get; set; }
        public uint CollisionSkillId { get; set; }
        public bool CollisionUnitOnly { get; set; }
        public float Damping { get; set; }
        public float Density { get; set; }
        public bool DisappearByCollision { get; set; }
        public uint FadeInDuration { get; set; }
        public uint FadeOutDuration { get; set; }
        public float FreeFallDamping { get; set; }
        public bool Graspable { get; set; }
        public float Gravity { get; set; }
        public uint LifeTime { get; set; }
        public float Mass { get; set; }
        public string ModelPath { get; set; }
        public string Name { get; set; }
        public bool NoGroundCollider { get; set; }
        public bool PushableByPlayer { get; set; }
        public uint SkillDelay { get; set; }
        public uint SkillId { get; set; }
        public uint SpawnDelay { get; set; }
        public float WaterDamping { get; set; }
        public float WaterDensity { get; set; }
        public float WaterResistance { get; set; }
    }
}
