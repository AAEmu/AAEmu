namespace AAEmu.Game.Models.Game.Expeditions
{
    public class ExpeditionConfig
    {
        public ExpeditionConfigCreate Create { get; set; }
        public string NameRegex { get; set; }
        public ExpeditionRolePolicy[] RolePolicies { get; set; }
    }

    public class ExpeditionConfigCreate
    {
        public int Cost { get; set; }
        public byte Level { get; set; }
        public byte PartyMemberCount { get; set; }
    }
}
