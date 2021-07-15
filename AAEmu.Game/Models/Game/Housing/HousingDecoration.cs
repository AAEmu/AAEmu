namespace AAEmu.Game.Models.Game.Housing
{
    public class HousingDecoration
    {
        public uint Id { get; set; }
        public string Name { get; set; }
        public bool AllowOnFloor { get; set; }
        public bool AllowOnWall { get; set; }
        public bool AllowOnCeiling { get; set; }
        public uint DoodadId { get; set; }
        public bool AllowPivotOnGarden { get; set; }
        public uint ActabilityGroupId { get; set; }
        public uint ActabilityUp { get; set; }
        public uint DecoActAbilityGroupId { get; set; }
        public bool AllowMeshOnGarden { get; set; }
    }
}
