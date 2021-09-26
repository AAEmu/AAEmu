namespace AAEmu.Game.Models.Game.Music
{
    public class SongData
    {
        public uint Id { get; set; }
        public uint AuthorId { get; set; }
        public string Title { get; set; }
        public string Song { get; set; }
        /// <summary>
        /// ItemId of the item used to create the Sheet Music, only used for upload validation
        /// </summary>
        public ulong SourceItemId { get; set; }
    }
}
