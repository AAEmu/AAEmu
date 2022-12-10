using System;
using System.Collections.Generic;
using System.Data;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Models.Stream
{
    public class CustomUcc : DefaultUcc
    {
        public override UccType Type => UccType.Complex ;
        public virtual List<byte> Data { get; set; } = new List<byte>();
        public bool SaveDataInDB = true;
        
        public override void Save(MySqlCommand command)
        {
            if (SaveDataInDB)
            {
                command.CommandText =
                    "REPLACE INTO `uccs` " +
                    "(`id`,`uploader_id`,`type`,`pattern1`,`pattern2`,`color1R`,`color1G`,`color1B`,`color2R`,`color2G`,`color2B`,`color3R`,`color3G`,`color3B`,`modified`,`data`) " +
                    "VALUES(@id, @uploader_id, @type, @pattern1, @pattern2, @color1R, @color1G, @color1B, @color2R, @color2G, @color2B, @color3R, @color3G, @color3B, @modified, @data)";
            }
            else
            {
                command.CommandText =
                    "REPLACE INTO `uccs` " +
                    "(`id`,`uploader_id`,`type`,`pattern1`,`pattern2`,`color1R`,`color1G`,`color1B`,`color2R`,`color2G`,`color2B`,`color3R`,`color3G`,`color3B`,`modified`) " +
                    "VALUES(@id, @uploader_id, @type, @pattern1, @pattern2, @color1R, @color1G, @color1B, @color2R, @color2G, @color2B, @color3R, @color3G, @color3B, @modified)";
            }

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
            if (SaveDataInDB)
                command.Parameters.AddWithValue("@data", Data.ToArray());
            command.ExecuteNonQuery();
        }
        
    }
}
