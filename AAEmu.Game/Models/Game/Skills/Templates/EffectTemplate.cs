using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units;

using NLog;

namespace AAEmu.Game.Models.Game.Skills.Templates
{
    public abstract class EffectTemplate
    {
        protected static Logger _log = LogManager.GetCurrentClassLogger();

        public uint Id { get; set; }

        public virtual uint BuffId => Id;

        public abstract bool OnActionTime { get; }

        public abstract void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj, CastAction castObj,
            EffectSource source, SkillObject skillObject, DateTime time, CompressedGamePackets packetBuilder = null);

        public virtual void Start(BaseUnit caster, BaseUnit owner, Buff buff)
        {
        }

        public virtual void TimeToTimeApply(BaseUnit caster, BaseUnit owner, Buff buff)
        {
        }

        public virtual void Dispel(BaseUnit caster, BaseUnit owner, Buff buff, bool replaced = false)
        {
        }

        public virtual int GetDuration(uint abLevel)
        {
            return 0;
        }

        public virtual double GetTick()
        {
            return 0;
        }

        public virtual void WriteData(PacketStream stream, uint abLevel)
        {
        }
    }
}
