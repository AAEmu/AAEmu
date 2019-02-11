using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Packets.G2C;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Models.Game.Char
{
    public class CharacterPortals
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();
        private Dictionary<uint, VisitedDistrict> VisitedDistricts { get; set; }

        public Dictionary<uint, Portal> PrivatePortals { get; set; }
        public Dictionary<uint, Portal> DistrictPortals { get; set; }
        public Character Owner { get; set; }

        public CharacterPortals(Character owner)
        {
            Owner = owner;
            PrivatePortals = new Dictionary<uint, Portal>();
            DistrictPortals = new Dictionary<uint, Portal>();
            VisitedDistricts = new Dictionary<uint, VisitedDistrict>();
        }

        public Portal GetPortalInfo(uint id)
        {
            if (DistrictPortals.ContainsKey(id))
                return DistrictPortals[id];
            return PrivatePortals.ContainsKey(id) ? PrivatePortals[id] : null;
        }

        public void NotifySubZone(Character owner, uint subZoneId)
        {
            if (!VisitedDistricts.ContainsKey(subZoneId))
            {
                var portal = PortalManager.Instance.GetPortalBySubZoneId(subZoneId);
                if (portal != null)
                {
                    DistrictPortals.Add(portal.Id, portal);
                    var newVisitedDistrict = new VisitedDistrict()
                    {
                        Id = VisitedSubZoneIdManager.Instance.GetNextId(),
                        SubZone = subZoneId,
                        Owner = Owner.Id
                    };
                    VisitedDistricts.Add(subZoneId, newVisitedDistrict);
                    Send();
                    _log.Info("{0}:{1} added to return district list ", portal.Name, subZoneId);
                }
            }
        }

        public void Send()
        {
            if (PrivatePortals.Count > 0)
            {
                var portals = new Portal[PrivatePortals.Count];
                PrivatePortals.Values.CopyTo(portals, 0);
                Owner.SendPacket(new SCCharacterPortalsPacket(portals));
            }

            if (DistrictPortals.Count > 0)
            {
                var portals = new Portal[DistrictPortals.Count];
                DistrictPortals.Values.CopyTo(portals, 0);
                Owner.SendPacket(new SCCharacterReturnDistrictsPacket(portals, 139)); // INFO - What is returnDistrictId?
            }
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
                        PrivatePortals.Add(template.Id, template);
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
                        var template = new VisitedDistrict
                        {
                            Id = reader.GetUInt32("id"),
                            SubZone = reader.GetUInt32("subzone"),
                            Owner = reader.GetUInt32("owner")
                        };
                        VisitedDistricts.Add(template.SubZone, template);
                    }
                }
            }

            if (VisitedDistricts.Count > 0)
            {
                foreach (var subZone in VisitedDistricts)
                {
                    var portal = PortalManager.Instance.GetPortalBySubZoneId(subZone.Key);
                    if (portal != null)
                    {
                        DistrictPortals.Add(portal.Id, portal);
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
                    command.Parameters.AddWithValue("@id", portal.Value.Id);
                    command.Parameters.AddWithValue("@name", portal.Value.Name);
                    command.Parameters.AddWithValue("@x", portal.Value.X);
                    command.Parameters.AddWithValue("@y", portal.Value.Y);
                    command.Parameters.AddWithValue("@z", portal.Value.Z);
                    command.Parameters.AddWithValue("@zone_id", portal.Value.ZoneId);
                    command.Parameters.AddWithValue("@z_rot", portal.Value.ZRot);
                    command.Parameters.AddWithValue("@owner", portal.Value.Owner);
                    command.ExecuteNonQuery();
                }
            }

            foreach (var (_, value) in VisitedDistricts)
            {
                using (var command = connection.CreateCommand())
                {
                    command.Connection = connection;
                    command.Transaction = transaction;

                    command.CommandText = "REPLACE INTO portal_visited_district(`id`,`subzone`,`owner`) VALUES (@id, @subzone, @owner)";
                    command.Parameters.AddWithValue("@id", value.Id);
                    command.Parameters.AddWithValue("@subzone", value.SubZone);
                    command.Parameters.AddWithValue("@owner", value.Owner);
                    command.ExecuteNonQuery();
                }
            }
        }
    }
}
