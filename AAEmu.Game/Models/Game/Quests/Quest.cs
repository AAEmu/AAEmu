using System;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.Quests
{
    public class Quest
    {
        public ulong Id { get; set; }
        public uint TemplateId { get; set; }
        public QuestTemplate Template { get; set; }
        public QuestStatus Status { get; set; }
        public int[] Objectives { get; set; }
        public byte Step { get; set; }
        public DateTime Time { get; set; }
        public Character Owner { get; set; }
        public int LeftTime => Time > DateTime.Now ? (int) (Time - DateTime.Now).TotalSeconds : -1;

        public Quest()
        {
            Objectives = new int[5];
        }

        public Quest(QuestTemplate template)
        {
            TemplateId = template.Id;
            Template = template;
            Objectives = new int[5];
        }
    }
}