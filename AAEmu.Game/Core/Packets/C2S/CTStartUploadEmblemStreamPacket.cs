using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.Stream;
using AAEmu.Game.Core.Network.Stream;
using AAEmu.Game.Models.Stream;

namespace AAEmu.Game.Core.Packets.C2S
{
    public class CTStartUploadEmblemStreamPacket : StreamPacket
    {
        public CTStartUploadEmblemStreamPacket() : base(CTOffsets.CTStartUploadEmblemStreamPacket)
        {
        }

        public override void Read(PacketStream stream)
        {
            var bc = stream.ReadBc();
            var UccId = stream.ReadInt64();
            var dataSize = stream.ReadInt32();
            // -----------------------

            _log.Warn("Create UCC Crest, printer bc:{0}, UccId:{1}, dataSize:{2}", bc, UccId, dataSize);
            
            // TODO: check if bc points to a Crest Printer (and you are nearby)

            if (dataSize == 0) // simple
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
                var customUcc = new CustomUcc() { UploaderId = Connection.GameConnection.ActiveChar.Id };
                customUcc.Read(stream);
                UccManager.Instance.StartUpload(Connection, dataSize, customUcc);
            }
        }
    }
}
