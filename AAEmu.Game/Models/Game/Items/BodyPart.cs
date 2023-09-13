using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items
{
    public class BodyPart : Item
    {
        public BodyPart()
        {
        }

        public BodyPart(ulong id, ItemTemplate template, int count) : base(id, template, count)
        {
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(TemplateId);
            return stream;
        }
    }
}
