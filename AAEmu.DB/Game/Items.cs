using System;
using System.Collections.Generic;

namespace AAEmu.DB.Game
{
    public partial class Items
    {
        public ulong Id { get; set; }
        public string Type { get; set; }
        public uint TemplateId { get; set; }
        public string SlotType { get; set; }
        public int Slot { get; set; }
        public int Count { get; set; }
        public byte[] Details { get; set; }
        public int LifespanMins { get; set; }
        public uint MadeUnitId { get; set; }
        public DateTime UnsecureTime { get; set; }
        public DateTime UnpackTime { get; set; }
        public uint Owner { get; set; }
        public byte? Grade { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
