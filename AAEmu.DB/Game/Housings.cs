using System;
using System.Collections.Generic;

namespace AAEmu.DB.Game
{
    public partial class Housings
    {
        public uint Id { get; set; }
        public uint AccountId { get; set; }
        public uint Owner { get; set; }
        public uint CoOwner { get; set; }
        public uint TemplateId { get; set; }
        public string Name { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public byte RotationZ { get; set; }
        public byte CurrentStep { get; set; }
        public int CurrentAction { get; set; }
        public byte Permission { get; set; }
    }
}
