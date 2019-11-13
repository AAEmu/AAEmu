using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Utils.DB;
using NLog;

namespace AAEmu.Game.Models.Game.Char
{
    public class CharacterPortals
    {
        private static readonly Logger _log = LogManager.GetCurrentClassLogger();
        private Dictionary<uint, VisitedDistrict> VisitedDistricts { get; set; }
        private readonly List<uint> _removedVisitedDistricts;
        private readonly List<uint> _removedPrivatePortals;

        public Dictionary<uint, Portal> PrivatePortals { get; set; }
        public Dictionary<uint, Portal> DistrictPortals { get; set; }
        public Character Owner { get; set; }

        public CharacterPortals(Character owner)
        {
            Owner = owner;
            PrivatePortals = new Dictionary<uint, Portal>();
            DistrictPortals = new Dictionary<uint, Portal>();
            VisitedDistricts = new Dictionary<uint, VisitedDistrict>();
            _removedVisitedDistricts = new List<uint>();
            _removedPrivatePortals = new List<uint>();
        }

        public Portal GetPortalInfo(uint id)
        {
            if (DistrictPortals.ContainsKey(id))
                return DistrictPortals[id];
            return PrivatePortals.ContainsKey(id) ? PrivatePortals[id] : null;
        }

        public void RemoveFromBookPortal(Portal portal, bool isPrivate)
        {
            if (isPrivate)
            {
                if (PrivatePortals.ContainsKey(portal.Id) && PrivatePortals.Remove(portal.Id))
                {
                    _removedPrivatePortals.Add(portal.Id);
                    //Owner.SendMessage("Recorded Portal deleted.");
                }
            }
            else
            {
                if (VisitedDistricts.ContainsKey(portal.SubZoneId) && VisitedDistricts.Remove(portal.SubZoneId))
                {
                    _removedVisitedDistricts.Add(portal.SubZoneId);
                    //Owner.SendMessage("Default Portal deleted.");
                }
            }
        }

        public void NotifySubZone(uint subZoneId)
        {
            if (!VisitedDistricts.ContainsKey(subZoneId))
            {
                var portal = PortalManager.Instance.GetPortalBySubZoneId(subZoneId);
                if (portal != null)
                {
                    var newVisitedDistrict = new VisitedDistrict()
                    {
                        Id = VisitedSubZoneIdManager.Instance.GetNextId(),
                        SubZone = subZoneId,
                        Owner = Owner.Id
                    };
                    VisitedDistricts.Add(subZoneId, newVisitedDistrict);
                    PopulateDistrictPortals();
                    Send();
                    _log.Info("{0}:{1} added to return district list ", portal.Name, subZoneId);
                }
            }
        }

        public void AddPrivatePortal(float x, float y, float z, uint zoneId, string name)
        {
            // TODO - Only working by command
            var newPortal = new Portal()
            {
                Id = PrivateBookIdManager.Instance.GetNextId(),
                Name = name,
                X = x,
                Y = y,
                Z = z,
                ZoneId = zoneId,
                ZRot = 0f,
                Owner = Owner.Id
            };
            PrivatePortals.Add(newPortal.Id, newPortal);
            Owner.SendPacket(new SCCharacterPortalsPacket(new[] { newPortal }));
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

        public void Load(GameDBContext ctx)
        {
            PrivatePortals = PrivatePortals.Concat(
                ctx.PortalBookCoords
                .Where(p => p.Owner == Owner.Id)
                .ToList()
                .Select(p => (Portal)p)
                .ToDictionary(p => p.Id, p => p)
                )
                .GroupBy(i => i.Key).ToDictionary(group => group.Key, group => group.First().Value);

            VisitedDistricts = VisitedDistricts.Concat(
               ctx.PortalVisitedDistrict
               .Where(p => p.Owner == Owner.Id)
               .ToList()
               .Select(p => (VisitedDistrict)p)
               .ToDictionary(p => p.SubZone, p => p)
               )
               .GroupBy(i => i.Key).ToDictionary(group => group.Key, group => group.First().Value);

            PopulateDistrictPortals();
        }

        public void Save(GameDBContext ctx)
        {
            if (_removedVisitedDistricts.Count > 0)
            {
                ctx.PortalVisitedDistrict.RemoveRange(
                    ctx.PortalVisitedDistrict.Where(p => p.Owner == Owner.Id && _removedVisitedDistricts.Contains((uint)p.Subzone)));
                _removedVisitedDistricts.Clear();
            }
            ctx.SaveChanges();

            if (_removedPrivatePortals.Count > 0)
            {
                ctx.PortalBookCoords.RemoveRange(
                    ctx.PortalBookCoords.Where(p => p.Owner == Owner.Id && _removedPrivatePortals.Contains((uint)p.Id)));
                _removedPrivatePortals.Clear();
            }
            ctx.SaveChanges();

            foreach (var value in PrivatePortals.Values)
            {
                ctx.PortalBookCoords.RemoveRange(
                    ctx.PortalBookCoords.Where(p =>
                        p.Id == value.Id &&
                        p.Owner == value.Owner));
            }
            ctx.SaveChanges();

            ctx.PortalBookCoords.AddRange(PrivatePortals.Values.Select(p => p.ToEntity()));
            ctx.SaveChanges();

            foreach (var value in VisitedDistricts.Values)
            {
                ctx.PortalVisitedDistrict.RemoveRange(
                    ctx.PortalVisitedDistrict.Where(p =>
                        p.Id == value.Id &&
                        p.Subzone == value.SubZone &&
                        p.Owner == value.Owner));
            }
            ctx.SaveChanges();
            ctx.PortalVisitedDistrict.AddRange(VisitedDistricts.Values.Select(p => p.ToEntity()));

            ctx.SaveChanges();
        }

        private void PopulateDistrictPortals()
        {
            DistrictPortals.Clear();
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
    }
}
