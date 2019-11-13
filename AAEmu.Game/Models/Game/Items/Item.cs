using System;
using AAEmu.Commons.Network;
using AAEmu.DB.Game;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items
{
    public class Item : PacketMarshaler
    {
        public byte WorldId { get; set; }
        public ulong Id { get; set; }
        public uint TemplateId { get; set; }
        public ItemTemplate Template { get; set; }
        public SlotType SlotType { get; set; }
        public int Slot { get; set; }
        public byte Grade { get; set; }
        public int Count { get; set; }
        public int LifespanMins { get; set; }
        public uint MadeUnitId { get; set; }
        public DateTime CreateTime { get; set; }
        public DateTime UnsecureTime { get; set; }
        public DateTime UnpackTime { get; set; }

        public virtual byte DetailType => 0; // TODO 1.0 max type: 8, at 1.2 max type 9 (size: 9 bytes)

        public Item()
        {
            WorldId = AppConfiguration.Instance.Id;
            Slot = -1;
        }

        public Item(byte worldId)
        {
            WorldId = worldId;
            Slot = -1;
        }

        public Item(ulong id, ItemTemplate template, int count)
        {
            WorldId = AppConfiguration.Instance.Id;
            Id = id;
            TemplateId = template.Id;
            Template = template;
            Count = count;
            Slot = -1;
        }

        public Item(byte worldId, ulong id, ItemTemplate template, int count)
        {
            WorldId = worldId;
            Id = id;
            TemplateId = template.Id;
            Template = template;
            Count = count;
            Slot = -1;
        }

        public static explicit operator Item(DB.Game.Items v)
        {
            var type = v.Type;
            Type nClass = null;
            try
            {
                nClass = Type.GetType(type);
            }
            catch (Exception ex)
            {
                _log.Error(ex, string.Format("Item type {0} not found!", type));
                return null;
            }

            Item item;
            try
            {
                item = (Item)Activator.CreateInstance(nClass);
            }
            catch (Exception ex)
            {
                _log.Error(ex);
                _log.Error(ex.InnerException);
                item = new Item();
            }

            item.Id             = v.Id           ;
            item.TemplateId     = v.TemplateId   ;
            item.Slot           = v.Slot         ;
            item.Count          = v.Count        ;
            item.LifespanMins   = v.LifespanMins ;
            item.MadeUnitId     = v.MadeUnitId   ;
            item.UnsecureTime   = v.UnsecureTime ;
            item.UnpackTime     = v.UnpackTime   ;
            item.CreateTime     = v.CreatedAt    ;

            item.Template = ItemManager.Instance.GetTemplate(item.TemplateId) ;
            item.SlotType = (SlotType)Enum.Parse(typeof(SlotType), v.SlotType, true);

            var details = (PacketStream) v.Details;
            item.ReadDetails(details);

            if (item.Template.FixedGrade >= 0)
                item.Grade = (byte)item.Template.FixedGrade; // Overwrite Fixed-grade items, just to make sure
            else if (item.Template.Gradable)
                item.Grade = (byte)v.Grade; // Load from our DB if the item is gradable

            return item;
        }

        public override void Read(PacketStream stream)
        {
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(TemplateId);
            // TODO ...
            // if (TemplateId == 0)
            //     return stream;
            stream.Write(Id);
            stream.Write(Grade);
            stream.Write((byte) 0); // flags
            stream.Write(Count);
            stream.Write(DetailType);
            WriteDetails(stream);
            stream.Write(CreateTime);
            stream.Write(LifespanMins);
            stream.Write(MadeUnitId);
            stream.Write(WorldId);
            stream.Write(UnsecureTime);
            stream.Write(UnpackTime);
            return stream;
        }

        public DB.Game.Items ToEntity(uint ownerId)
        {
            var details = new PacketStream();
            this.WriteDetails(details);
            DB.Game.Items item = new DB.Game.Items()
            {
                Id           = this.Id                   ,
                Type         = this.GetType().ToString() ,
                TemplateId   = this.TemplateId           ,
                SlotType     = this.SlotType.ToString()  ,
                Slot         = this.Slot                 ,
                Count        = this.Count                ,
                Details      = details.GetBytes()        ,
                LifespanMins = this.LifespanMins         ,
                MadeUnitId   = this.MadeUnitId           ,
                UnsecureTime = this.UnsecureTime         ,
                UnpackTime   = this.UnpackTime           ,
                CreatedAt    = this.CreateTime           ,
                Grade        = this.Grade                ,
                Owner        = ownerId                   ,
            };
            return item;
        }

        public virtual void ReadDetails(PacketStream stream)
        {
        }

        public virtual void WriteDetails(PacketStream stream)
        {
        }
    }
}
