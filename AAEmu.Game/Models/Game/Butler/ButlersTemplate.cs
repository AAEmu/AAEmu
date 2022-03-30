namespace AAEmu.Game.Models.Game.Butler
{
    public class ButlersTemplate
    {
        // TODO нужна база данных, что бы правильно было
        public uint Id { get; set; }
        public string Name { get; set; }
        public uint MainModelId { get; set; }
        public uint ItemId { get; set; }
        public float SpawnOffsetFront { get; set; }
        public float SpawnOffsetZ { get; set; }
        public int BuildRadius { get; set; }
        public int TaxDuration { get; set; }
        public uint OriginItemId { get; set; }
        public int TaxationId { get; set; }

        public ButlersTemplate()
        {
        }
    }
}
