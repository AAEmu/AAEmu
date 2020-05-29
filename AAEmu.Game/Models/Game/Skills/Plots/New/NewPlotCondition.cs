namespace AAEmu.Game.Models.Game.Skills.Plots.New
{
    public class NewPlotCondition
    {
        public int Id { get; set; }
        public bool NotCondition { get; set; }
        public int Kind { get; set; }
        public int Param1 { get; set; }
        public int Param2 { get; set; }
        public int Param3 { get; set; }

        public bool Execute()
        {
            return false;
        }
    }
}
