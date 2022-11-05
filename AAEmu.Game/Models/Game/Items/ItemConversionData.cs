using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.Items
{
    public class ItemConversionReagent
    {
        public int ConversionSet;
        public uint ConversionId;
        public ItemImplEnum ImplId;
        public uint InputItemId;
        public int MinLevel;
        public int MaxLevel;
        public byte MinItemGrade;
        public byte MaxItemGrade;
    }

    public class ItemConversionProduct
    {
        public uint ConversionId;
        public int ChanceRate;
        public uint OuputItemId;
        public int Weight;
        public int MinOutput;
        public int MaxOutput;
    }
}