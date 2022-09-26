namespace AAEmu.Game.Models.Game.Items.Templates
{
    public class AttributeModifiers
    {
        public uint Id { get; set; }
        public int StrWeight { get; set; }
        public int DexWeight { get; set; }
        public int StaWeight { get; set; }
        public int IntWeight { get; set; }
        public int SpiWeight { get; set; }
        public int AllWeight => StrWeight + DexWeight + StaWeight + IntWeight + SpiWeight;

        public int Count
        {
            get
            {
                var res = 0;
                if (StrWeight > 0)
                {
                    res++;
                }

                if (DexWeight > 0)
                {
                    res++;
                }

                if (StaWeight > 0)
                {
                    res++;
                }

                if (IntWeight > 0)
                {
                    res++;
                }

                if (SpiWeight > 0)
                {
                    res++;
                }

                return res;
            }
        }
    }
}