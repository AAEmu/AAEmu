﻿namespace AAEmu.Game.Models.Game.Slaves
{
    public class SlaveDoodadBindings
    {
        public uint Id { get; set; }
        public uint OwnerId { get; set; }
        public string OwnerType { get; set; }
        public int AttachPointId { get; set; }
        public uint DoodadId { get; set; }
        public bool Persist { get; set; }
        public float Scale { get; set; }
    }
}
