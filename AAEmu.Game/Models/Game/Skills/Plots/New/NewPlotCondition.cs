namespace AAEmu.Game.Models.Game.Skills.Plots.New
{
    public class NewPlotCondition
    {
        public int Id { get; set; }
        public bool NotCondition { get; set; }
        public PlotConditionType Kind { get; set; }
        public int Param1 { get; set; }
        public int Param2 { get; set; }
        public int Param3 { get; set; }

        public bool Execute()
        {
            var result = true;
            
            return NotCondition ? !result : result;
        }
    }
}
