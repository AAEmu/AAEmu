using System;
using System.Collections.Generic;

namespace AAEmu.DB.Game
{
    public partial class Quests
    {
        public long Id { get; set; }
        public uint TemplateId { get; set; }
        public byte[] Data { get; set; }
        public byte Status { get; set; }
        public uint Owner { get; set; }
    }
}
