using System;
using AAEmu.Commons.Network;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Models.Stream
{
    public class Ucc : PacketMarshaler
    {
        public uint Id { get; set; }
        public uint UploaderId { get; set; }
        public DateTime Modified { get; set; }
        public virtual UccType Type { get; set; }
        public virtual void Save(MySqlCommand cmd) {}
        public virtual void Load(MySqlCommand cmd) {}
    }

    public enum UccType
    {
        Simple = 1,
        Complex = 2
    }
}
