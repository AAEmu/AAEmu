using System.Collections.Generic;

namespace AAEmu.Game.Models.Game.DoodadObj.Templates
{
    public class DoodadTemplate
    {
        public uint Id { get; set; }
        public bool OnceOneMan { get; set; }
        public bool OnceOneInteraction { get; set; }
        public bool MgmtSpawn { get; set; }
        public int Percent { get; set; }
        public int MinTime { get; set; }
        public int MaxTime { get; set; }
        public uint ModelKindId { get; set; }
        public bool UseCreatorFaction { get; set; }
        public bool ForceTodTopPriority { get; set; }
        public uint MilestoneId { get; set; }
        public uint GroupId { get; set; }
        public bool UseTargetDecal { get; set; }
        public bool UseTargetSilhouette { get; set; }
        public bool UseTargetHighlight { get; set; }
        public float TargetDecalSize { get; set; }
        public int SimRadius { get; set; }
        public bool CollideShip { get; set; }
        public bool CollideVehicle { get; set; }
        public uint ClimateId { get; set; }
        public bool SaveIndun { get; set; }
        public bool ForceUpAction { get; set; }
        public bool Parentable { get; set; }
        public bool Childable { get; set; }
        public uint FactionId { get; set; }
        public int GrowthTime { get; set; }
        public bool DespawnOnCollision { get; set; }
        public bool NoCollision { get; set; }
        public uint RestrictZoneId { get; set; }

        public List<DoodadFuncGroups> FuncGroups { get; set; }

        public DoodadTemplate()
        {
            FuncGroups = new List<DoodadFuncGroups>();
        }

        /// <summary>
        /// There's probably a better why to check this
        /// </summary>
        /// <returns>Returns true if the GroupId is one of ones that give vocation badges when used</returns>
        public bool GrantsVocationWhenUsed()
        {
            // TODO: Need to remove magic numbers
            switch (GroupId)
            {
                case 2: // Deforestation - Trees
                case 3: // Picking - Herbs
                case 4: // Mining - Minerals
                case 5: // Livestock - Livestock
                case 12: // Agriculture - Crops
                case 39: //Interaction - Excavation
                case 40: // Agriculture - Marine Crops
                case 65: // Fish (sports fishing ?)
                    return true;
                default:
                    return false;
            }
        }
    }
}
