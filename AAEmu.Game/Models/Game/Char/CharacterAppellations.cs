using System.Collections.Generic;
using AAEmu.Game.Core.Packets.G2C;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Models.Game.Char
{
    public class CharacterAppellations
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        
        public List<uint> Appellations { get; set; }
        public uint ActiveAppellation { get; set; }

        public Character Owner { get; set; }

        public CharacterAppellations(Character owner)
        {
            Owner = owner;
            Appellations = new List<uint>();
            ActiveAppellation = 0;
        }

        public void Add(uint id)
        {
            // SCAppellationGainedPacket
            if (Appellations.Contains(id))
            {
                Log.Warn("Duplicate add {0}, ownerId {1}", id, Owner.Id);
                return;
            }

            Appellations.Add(id);
            Owner.SendPacket(new SCAppellationGainedPacket(id));
        }

        public void Change(uint id)
        {
            if (id == 0)
            {
                // TODO remove buff
                ActiveAppellation = 0;
            }
            else
            {
                if (Appellations.Contains(id))
                {
                    ActiveAppellation = id;
                    // TODO add/change buff if exist in template
                }
                else
                {
                    Log.Warn("Id {0} doesn't exist, owner {1}", id, Owner.Id);
                }
            }

            Owner.BroadcastPacket(new SCAppellationChangedPacket(Owner.ObjId, ActiveAppellation), true);
        }

        public void Send()
        {
            for (var i = 0; i < Appellations.Count; i += 512)
            {
                var result = new (uint, bool)[Appellations.Count - i <= 512 ? Appellations.Count - i : 512];

                for (var j = 0; j < result.Length; j++)
                    result[j] = (Appellations[i + j], Appellations[i + j] == ActiveAppellation);

                Owner.SendPacket(new SCAppellationsPacket(result));
            }
        }

        public void Load(MySqlConnection connection)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT id, active FROM appellations WHERE `owner` = @owner";
                command.Parameters.AddWithValue("@owner", Owner.Id);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var id = reader.GetUInt32("id");
                        var active = reader.GetBoolean("active");

                        Appellations.Add(id);
                        if (active)
                            ActiveAppellation = id; // TODO нужно повесить баф
                    }
                }
            }
        }

        public void Save(MySqlConnection connection, MySqlTransaction transaction)
        {
            foreach (var appellation in Appellations)
            {
                using (var command = connection.CreateCommand())
                {
                    command.Connection = connection;
                    command.Transaction = transaction;

                    command.CommandText = "REPLACE INTO appellations(`id`,`active`,`owner`) VALUES (@id, @active, @owner)";
                    command.Parameters.AddWithValue("@id", appellation);
                    command.Parameters.AddWithValue("@active", ActiveAppellation == appellation);
                    command.Parameters.AddWithValue("@owner", Owner.Id);
                    command.ExecuteNonQuery();
                }
            }
        }
    }
}
