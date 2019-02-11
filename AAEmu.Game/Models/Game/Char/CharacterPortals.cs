using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Models.Game.Char
{
    public class CharacterPortals
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();

        public List<Portal> PrivatePortals { get; set; }
        public List<Portal> DistrictPortals { get; set; }

        private VisitedDistricts VisitedDistricts { get; set; }

        public Character Owner { get; set; }

        public CharacterPortals(Character owner)
        {
            Owner = owner;
            PrivatePortals = new List<Portal>();
            DistrictPortals = new List<Portal>();
            VisitedDistricts = null;
        }

        public Portal GetPortalInfo(uint id)
        {
            if (DistrictPortals.Any(x => x.Id == id))
                return DistrictPortals.First(y => y.Id == id);
            else if (PrivatePortals.Any(x => x.Id == id))
                return DistrictPortals.First(y => y.Id == id);
            else
                return null;
        }

        public void NotifySubZone(uint subZoneId)
        {
            var alreadyVisited = VisitedDistricts.VisitedSubZones.Any(x => x == subZoneId);
            if (!alreadyVisited)
            {
                var portal = PortalManager.Instance.GetPortalBySubZoneId(subZoneId);
                if (portal != null)
                {
                    DistrictPortals.Add(portal);
                    VisitedDistricts.VisitedSubZones.Add(subZoneId);
                    Send();
                    _log.Info("{0}:{1} added to return district list ", portal.Name, subZoneId);
                }
            }
        }

        public void Send()
        {
            if (PrivatePortals.Count > 0)
                Owner.SendPacket(new SCCharacterPortalsPacket(PrivatePortals.ToArray()));
            if (DistrictPortals.Count > 0)
                Owner.SendPacket(new SCCharacterReturnDistrictsPacket(DistrictPortals.ToArray(), 0));
        }

        public void Load(MySqlConnection connection)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM portal_book_coords WHERE `owner` = @owner";
                command.Parameters.AddWithValue("@owner", Owner.Id);
                command.Prepare();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var template = new Portal
                        {
                            Id = reader.GetUInt32("id"),
                            Name = reader.GetString("name"),
                            X = reader.GetFloat("x"),
                            Y = reader.GetFloat("y"),
                            Z = reader.GetFloat("z"),
                            ZoneId = reader.GetUInt32("zone_id"),
                            ZRot = reader.GetFloat("z_rot"),
                            SubZoneId = reader.GetUInt32("sub_zone_id"),
                            Owner = reader.GetUInt32("owner")
                        };
                        PrivatePortals.Add(template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM portal_visited_district WHERE `owner` = @owner";
                command.Parameters.AddWithValue("@owner", Owner.Id);
                command.Prepare();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var ordinal = reader.GetOrdinal("visited_sub_zones");
                        var subZoneString = reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
                        VisitedDistricts = new VisitedDistricts
                        {
                            Id = reader.GetUInt32("id"),
                            VisitedSubZones = subZoneString.Split(" ").Select(uint.Parse).ToList(),
                            Owner = reader.GetUInt32("owner")
                        };
                    }
                }
            }

            if (VisitedDistricts != null)
            {
                foreach (var subZone in VisitedDistricts.VisitedSubZones)
                {
                    var portal = PortalManager.Instance.GetPortalBySubZoneId(subZone);
                    if (portal != null)
                    {
                        DistrictPortals.Add(portal);
                    }
                }
            }
        }

        public void Save(MySqlConnection connection, MySqlTransaction transaction)
        {
            foreach (var portal in PrivatePortals)
            {
                using (var command = connection.CreateCommand())
                {
                    command.Connection = connection;
                    command.Transaction = transaction;

                    command.CommandText = "REPLACE INTO portal_book_coords(`id`,`name`,`x`,`y`,`z`,`zone_id`,`z_rot`,`owner`) VALUES (@id, @name, @x, @y, @z, @zone_id, @z_rot, @owner)";
                    command.Parameters.AddWithValue("@id", portal.Id);
                    command.Parameters.AddWithValue("@name", portal.Name);
                    command.Parameters.AddWithValue("@x", portal.X);
                    command.Parameters.AddWithValue("@y", portal.Y);
                    command.Parameters.AddWithValue("@z", portal.Z);
                    command.Parameters.AddWithValue("@zone_id", portal.ZoneId);
                    command.Parameters.AddWithValue("@z_rot", portal.ZRot);
                    command.Parameters.AddWithValue("@owner", portal.Owner);
                    command.ExecuteNonQuery();
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;

                command.CommandText = "REPLACE INTO portal_visited_district(`id`,`visited_sub_zones`,`owner`) VALUES (@id, @visited_sub_zones, @owner)";
                command.Parameters.AddWithValue("@id", VisitedDistricts.Id);
                var visitedSubZones = string.Empty;
                if (VisitedDistricts.VisitedSubZones.Count > 0)
                {
                    visitedSubZones = string.Join(" ", VisitedDistricts.VisitedSubZones.Select(x => x.ToString()).ToArray());
                }
                command.Parameters.AddWithValue("@visited_sub_zones", visitedSubZones);
                command.Parameters.AddWithValue("@owner", VisitedDistricts.Owner);
                command.ExecuteNonQuery();
            }
        }
    }
}
