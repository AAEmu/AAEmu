using System.Collections.Generic;

using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Achievement
{
    public partial class Achievements
    {
        public Achievements()
        {
            AchievementObjectives = new HashSet<AchievementObjectives>();
        }

        public uint Id { get; set; }
        public uint AppellationId { get; set; }
        public uint SubCategoryId { get; set; }
        //public uint CategoryId { get; set; }
        public uint CompleteNum { get; set; }
        public bool CompleteOr { get; set; }
        //public string Description { get; set; }
        public uint GradeId { get; set; }
        public uint IconId { get; set; }
        public bool IsActive { get; set; }
        public bool IsHidden { get; set; }
        public uint ItemNum { get; set; }
        public uint ItemId { get; set; }
        //public string Name { get; set; }
        public bool OrUnitReqs { get; set; }
        public uint ParentAchievementId { get; set; }
        public uint Priority { get; set; }
        public bool SeasonOff { get; set; }
        //public string Summary { get; set; }

        //public virtual Icons Icon { get; set; }
        public virtual ItemTemplate Item { get; set; }
        public virtual ICollection<AchievementObjectives> AchievementObjectives { get; set; }
    }
}
