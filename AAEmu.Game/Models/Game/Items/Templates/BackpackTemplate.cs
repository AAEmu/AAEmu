using System;

namespace AAEmu.Game.Models.Game.Items.Templates
{
    public enum BackpackType {
        CastleClaim = 1,
        Glider = 2,
        TradePack = 3,
        SiegeDeclare = 4,
        NationFlag = 5,
        Fish = 6,
        ToyFlag = 7
    }

    public class BackpackTemplate : ItemTemplate
    {
        public override Type ClassType => typeof(Backpack);

        public uint AssetId { get; set; }
        public BackpackType BackpackType { get; set; }
        public uint DeclareSiegeZoneGroupId { get; set; }
        public bool Heavy { get; set; } 
        public uint Asset2Id { get; set; }
        public bool NormalSpeciality { get; set; }
        public bool UseAsStat { get; set; }
        public uint SkinKindId { get; set; }
    }
}
