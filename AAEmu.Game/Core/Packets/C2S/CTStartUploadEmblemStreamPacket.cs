using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.Stream;
using AAEmu.Game.Core.Network.Stream;
using AAEmu.Game.Models.Stream;

namespace AAEmu.Game.Core.Packets.C2S
{
    public class CTStartUploadEmblemStreamPacket : StreamPacket
    {
        public CTStartUploadEmblemStreamPacket() : base(0x0E)
        {
        }

        public override void Read(PacketStream stream)
        {
            var bc = stream.ReadBc();
            var type = stream.ReadInt64();
            var total = stream.ReadInt32();
            // -----------------------


            if (total == 0) // simple
            {
                var defaultUcc = new DefaultUcc()
                {
                    UploaderId = Connection.GameConnection.ActiveChar.Id
                };
                defaultUcc.Read(stream);
                UccManager.Instance.AddDefaultUcc(defaultUcc, Connection);
            }
            else // complex
            {
                UccManager.Instance.StartUpload(Connection);
            }
        }
    }
}
