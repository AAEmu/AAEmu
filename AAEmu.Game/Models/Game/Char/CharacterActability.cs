using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Utils.DB;

namespace AAEmu.Game.Models.Game.Char
{
    public class CharacterActability
    {
        public Dictionary<uint, Actability> Actabilities { get; set; }

        public Character Owner { get; set; }

        public CharacterActability(Character owner)
        {
            Owner = owner;
            Actabilities = new Dictionary<uint, Actability>();
        }

        public void AddPoint(uint id, int point)
        {
            if (!Actabilities.ContainsKey(id))
                return;
            
            var actability = Actabilities[id];
            actability.Point += point;

            var template = CharacterManager.Instance.GetExpertLimit(actability.Step);
            if (actability.Point > template.UpLimit)
                actability.Point = template.UpLimit;
        }

        public void Regrade(uint id, bool isUpgrade)
        {
            var actability = Actabilities[id];

            // TODO add validation to expert limit, if expert_limit = 0 -> infinity

            if (isUpgrade)
            {
                var template = CharacterManager.Instance.GetExpertLimit(actability.Step);
                if (template == null)
                    return; // TODO ... send msg error?

                if (actability.Point < template.UpLimit)
                    return; // TODO ... send msg error?

                actability.Step++;
            }
            else
            {
                var template = CharacterManager.Instance.GetExpertLimit(actability.Step - 1);
                if (template == null)
                    return; // TODO ... send msg error?

                actability.Step--;
                actability.Point = template.UpLimit;
            }

            Owner.SendPacket(new SCExpertLimitModifiedPacket(isUpgrade, id, actability.Step));
        }

        public void ExpandExpert()
        {
            var expand = CharacterManager.Instance.GetExpandExpertLimit(Owner.ExpandedExpert);
            if (expand == null)
                return; // TODO ... send msg error?

            if (expand.LifePoint > Owner.VocationPoint)
                return; // TODO ... send msg error?

            if (expand.ItemId != 0 && expand.ItemCount != 0 && !Owner.Inventory.CheckItems(expand.ItemId, expand.ItemCount))
                return; // TODO ... send msg error?

            if (expand.LifePoint > 0)
            {
                Owner.VocationPoint -= expand.LifePoint;
                Owner.SendPacket(new SCGamePointChangedPacket(1, -expand.LifePoint));
            }

            if (expand.ItemId != 0 && expand.ItemCount != 0)
            {
                var items = Owner.Inventory.RemoveItem(expand.ItemId, expand.ItemCount);

                var tasks = new List<ItemTask>();
                foreach (var (item, count) in items)
                {
                    if (item.Count == 0)
                        tasks.Add(new ItemRemove(item));
                    else
                        tasks.Add(new ItemCountUpdate(item, -count));
                }

                Owner.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.ExpandExpert, tasks, new List<ulong>()));
            }

            Owner.ExpandedExpert = expand.ExpandCount;
            Owner.SendPacket(new SCExpertExpandedPacket(Owner.ExpandedExpert));
        }

        public void Send()
        {
            Owner.SendPacket(new SCActabilityPacket(true, Actabilities.Values.ToArray()));
        }

        public void Load(GameDBContext ctx)
        {
            Actabilities = Actabilities.Concat(
                ctx.Actabilities
                .Where(a => a.Owner == Owner.Id)
                .ToList()
                .Select(a=>(Actability)a)
                .ToDictionary(a=>a.Id,a=>a)
                )
                .GroupBy(i => i.Key).ToDictionary(group => group.Key, group => group.First().Value);
        }

        public void Save(GameDBContext ctx)
        {
            List<byte> ids = Actabilities.Values.Select(a => (byte)a.Id).ToList();

            ctx.Actabilities.RemoveRange(
                ctx.Actabilities.Where(a => ids.Contains((byte)a.Id) && a.Owner == Owner.Id));
            ctx.SaveChanges();

            ctx.Actabilities.AddRange(
                Actabilities.Values.Select(a => a.ToEntity(Owner.Id)));

            ctx.SaveChanges();
        }
    }
}
