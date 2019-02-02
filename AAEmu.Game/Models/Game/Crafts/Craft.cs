using System;
using System.Collections.Generic;
using System.Text;

namespace AAEmu.Game.Models.Game.Crafts
{
    /*
        Data relating to a craft.
    */
    public class Craft
    {
        public uint Id {get; set;}
        public int CastDelay {get; set;}
        public int ToolId {get; set;}
        public int SkillId {get; set;}
        public int WiId {get; set;}
        public int MilestoneId {get; set;}
        public int ReqDoodadId {get; set;}
        public bool NeedBind {get; set;}
        public int AcId {get; set;}
        public int ActabilityLimit {get; set;}
        public bool ShowUpperCraft {get; set;}
        public int RecommendLevel {get; set;}
        public int VisibleOrder {get; set;}
    }
}
