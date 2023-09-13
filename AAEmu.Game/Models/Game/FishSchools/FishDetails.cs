namespace AAEmu.Game.Models.Game.FishSchools
{
    public class FishDetails
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public uint ItemId { get; set; }
        public int MinWeight { get; set; }
        public int MaxWeight { get; set; }
        public int MinLength { get; set; }
        public int MaxLength { get; set; }

        /*
           id
           name
           item_id
           min_weight
           max_weight
           min_length
           max_length
        */
    }
}