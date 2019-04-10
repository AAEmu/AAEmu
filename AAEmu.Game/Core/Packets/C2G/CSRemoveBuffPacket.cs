using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRemoveBuffPacket : GamePacket
    {
        public CSRemoveBuffPacket() : base(0x055, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var objId = stream.ReadBc();
            var buffId = stream.ReadUInt32();
            var reason = stream.ReadByte();

            if (Connection.ActiveChar.ObjId != objId)
                return;
            var effect = Connection.ActiveChar.Effects.GetEffectByIndex(buffId);
            if (effect == null)
                return;
            if (effect.Template is BuffTemplate template)
                if (template.Kind == BuffKind.Good)
                    effect.Exit();
            if (effect.Template is BuffEffect buffEffect)
                if (buffEffect.Buff.Kind == BuffKind.Good)
                    effect.Exit();
        }
    }
}
