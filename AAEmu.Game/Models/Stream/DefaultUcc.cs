using System;
using AAEmu.Commons.Network;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Models.Stream
{
    public class DefaultUcc : Ucc
    {
        public override UccType Type => UccType.Simple;
        public uint Pattern1 { get; set; }
        public uint Pattern2 { get; set; }
        
        public uint Color1R { get; set; }
        public uint Color1G { get; set; }
        public uint Color1B { get; set; }
        
        public uint Color2R { get; set; }
        public uint Color2G { get; set; }
        public uint Color2B { get; set; }
        
        public uint Color3R { get; set; }
        public uint Color3G { get; set; }
        public uint Color3B { get; set; }

        public override void Save(MySqlCommand command)
        {
            command.CommandText =
                "REPLACE INTO `uccs` " +
                "(`id`,`uploader_id`,`type`,`pattern1`,`pattern2`,`color1R`,`color1G`,`color1B`,`color2R`,`color2G`,`color2B`,`color3R`,`color3G`,`color3B`,`modified`) " +
                "VALUES(@id, @uploader_id, @type, @pattern1, @pattern2, @color1R, @color1G, @color1B, @color2R, @color2G, @color2B, @color3R, @color3G, @color3B, @modified)";

            command.Parameters.AddWithValue("@id", Id);
            command.Parameters.AddWithValue("@uploader_id", UploaderId);
            command.Parameters.AddWithValue("@type", (byte)Type);
            command.Parameters.AddWithValue("@pattern1", Pattern1);
            command.Parameters.AddWithValue("@pattern2", Pattern2);
            command.Parameters.AddWithValue("@color1R", Color1R);
            command.Parameters.AddWithValue("@color1G", Color1G);
            command.Parameters.AddWithValue("@color1B", Color1B);
            command.Parameters.AddWithValue("@color2R", Color2R);
            command.Parameters.AddWithValue("@color2G", Color2G);
            command.Parameters.AddWithValue("@color2B", Color2B);
            command.Parameters.AddWithValue("@color3R", Color3R);
            command.Parameters.AddWithValue("@color3G", Color3G);
            command.Parameters.AddWithValue("@color3B", Color3B);
            command.Parameters.AddWithValue("@modified", DateTime.UtcNow);
            command.ExecuteNonQuery();
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(Pattern1);
            stream.Write(Pattern2);
            stream.Write(Color1R);
            stream.Write(Color1G);
            stream.Write(Color1B);
            stream.Write(Color2R);
            stream.Write(Color2G);
            stream.Write(Color2B);
            stream.Write(Color3R);
            stream.Write(Color3G);
            stream.Write(Color3B);
            stream.Write(Modified.ToBinary());
            return stream;
        }

        public override void Read(PacketStream stream)
        {
            Pattern1 = stream.ReadUInt32();
            Pattern2 = stream.ReadUInt32();
            Color1R = stream.ReadUInt32();
            Color1G = stream.ReadUInt32();
            Color1B = stream.ReadUInt32();
            Color2R = stream.ReadUInt32();
            Color2G = stream.ReadUInt32();
            Color2B = stream.ReadUInt32();
            Color3R = stream.ReadUInt32();
            Color3G = stream.ReadUInt32();
            Color3B = stream.ReadUInt32();
            Modified = DateTime.FromBinary(stream.ReadInt64());
        }
    }
}
