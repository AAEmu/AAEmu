namespace AAEmu.Game.Models.Game.Items
{
    public class ItemConversionReagent
    {
        public int ConversionSet;
        public uint ConversionId;
        public uint ImplId;
        public ulong InputItemId;
        public int MinLevel;
        public int MaxLevel;
        public byte MinItemGrade;
        public byte MaxItemGrade;
    }

    public class ItemConversionProduct
    {
        public uint ConversionId;
        public uint ChanceRate;
        public ulong OuputItemId;
        public uint Weight;
        public int MinOutput;
        public int MaxOutput;
    }
}