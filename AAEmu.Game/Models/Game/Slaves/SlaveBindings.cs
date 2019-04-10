namespace AAEmu.Game.Models.Game.Slaves
{
    public class SlaveBindings
    {
        public int Id { get; set; }
        public int OwnerId { get; set; }
        public string OwnerType { get; set; }
        public int SlaveId { get; set; }
        public int AttachPointId { get; set; }
    }
}
