using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Utils.DB;
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

        public void Load(GameDBContext ctx)
        {
            var aps = ctx.Appellations.Where(a => a.Owner == Owner.OwnerId).ToList();
            Appellations.AddRange(aps.Select(a => (uint)a.Id));

            // Preserving original comments as they may (or may not) be important.
            // TODO нужно повесить баф
            ActiveAppellation = (uint)(aps.Where(a => a.Active == 1).FirstOrDefault()?.Id ?? 0);
        }

        public void Save(GameDBContext ctx)
        {
            ctx.Appellations.RemoveRange(
                ctx.Appellations.Where(a => Appellations.Contains((uint)a.Id) && a.Owner == Owner.Id));
            ctx.SaveChanges();

            // TODO: move convertions into ToEntity() function after creating Appelation entity.
            ctx.Appellations.AddRange(
                Appellations.Select(a => new DB.Game.Appellations() { 
                    Id      = (int) a,
                    Active  = (byte) (ActiveAppellation == a ? 1 : 0),
                    Owner   = (int) Owner.Id
                }));

            ctx.SaveChanges();
        }
    }
}
