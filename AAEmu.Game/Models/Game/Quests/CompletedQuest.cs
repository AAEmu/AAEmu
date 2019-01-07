using System.Collections;

namespace AAEmu.Game.Models.Game.Quests
{
    public class CompletedQuest
    {
        public ushort Id { get; set; }
        public BitArray Body { get; set; }

        public CompletedQuest()
        {
        }

        public CompletedQuest(ushort id)
        {
            Id = id;
            Body = new BitArray(64);
        }
    }
}