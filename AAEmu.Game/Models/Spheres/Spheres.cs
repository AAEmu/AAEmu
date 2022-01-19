namespace AAEmu.Game.Models.Spheres
{
    public class Spheres
    {
        public uint Id { get; set; }
        public string Name { get; set; }
        public bool EnterOrLeave { get; set; }
        public uint SphereDetailId { get; set; }
        public string SphereDetailType { get; set; }
        public uint TriggerConditionId { get; set; }
        public uint TriggerConditionTime { get; set; }
        public string TeamMsg { get; set; }
        public uint CategoryId { get; set; }
        public bool OrUnitReqs { get; set; }
        public bool IsPersonalMsg { get; set; }
        public uint MilestoneId { get; set; }
        public bool NameTr { get; set; }
        public bool TeamMsgTr { get; set; }
    }
}
