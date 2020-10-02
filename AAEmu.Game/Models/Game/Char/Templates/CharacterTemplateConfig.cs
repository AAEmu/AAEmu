namespace AAEmu.Game.Models.Game.Char.Templates
{
    public class CharacterTemplateConfig
    {
        public class Position
        {
            public uint WorldId { get; set; }
            public float X { get; set; }
            public float Y { get; set; }
            public float Z { get; set; }
            public sbyte RotationX { get; set; }
            public sbyte RotationY { get; set; }
            public sbyte RotationZ { get; set; }

        }

        public uint Id { get; set; }
        public string Name { get; set; }
        public Position Pos { get; set; }

        public byte NumInventorySlot { get; set; }
        public short NumBankSlot { get; set; }
    }
}
