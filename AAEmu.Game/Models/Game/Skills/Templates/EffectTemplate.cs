namespace AAEmu.Game.Models.Game.Skills.Templates
{
    public abstract class EffectTemplate
    {
        public uint Id { get; set; }

        public virtual uint BuffId => Id;

        public abstract bool OnActionTime { get; }
    }
}