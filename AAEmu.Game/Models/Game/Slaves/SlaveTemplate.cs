using System.Collections.Generic;

namespace AAEmu.Game.Models.Game.Slaves
{
    public class SlaveTemplate
    {
        public uint Id { get; set; }
        public string Name { get; set; }
        public uint ModelId { get; set; }
        public bool Mountable { get; set; }
        public float SpawnXOffset { get; set; }
        public float SpawnYOffset { get; set; }
        public uint FactionId { get; set; }
        public uint Level { get; set; }
        public int Cost { get; set; }
        public SlaveKind SlaveKind { get; set; }
        public uint SpawnValidAreaRance { get; set; }
        public uint SlaveInitialItemPackId { get; set; }
        public uint SlaveCustomizingId { get; set; }
        public bool Customizable { get; set; }

        public List<SlaveInitialBuffs> InitialBuffs { get; }
        public List<SlavePassiveBuffs> PassiveBuffs { get; }
        public List<SlaveDoodadBindings> DoodadBindings { get; }

        public SlaveTemplate()
        {
            InitialBuffs = new List<SlaveInitialBuffs>();
            PassiveBuffs = new List<SlavePassiveBuffs>();
            DoodadBindings = new List<SlaveDoodadBindings>();
        }
    }
}
