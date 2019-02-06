using System;
using System.Collections.Generic;
using System.Text;

namespace AAEmu.Game.Models.Game.Housing
{
    public class Housing
    {
        public uint designId { get; set; }
        public string name { get; set; }
        public uint categoryId { get; set; }
        public uint mainmodelId { get; set; }
        public uint doormodelId { get; set; }
        public uint stairmodelId { get; set; }
        public bool autoZ { get; set; }
        public bool gateexists { get; set; }
        public uint hp { get; set; }
        public uint repaircost { get; set; }
        public float gardenraidus { get; set; }
        public string family { get; set; }
        public uint taxationId { get; set; }
        public uint guardtowersettingId { get; set; }
        public uint cinemaId { get; set; }
        public float cinemaradius { get; set; }
        public float autoZoffsetX { get; set; }
        public float autoZoffsetY { get; set; }
        public float autoZoffsetZ { get; set; }
        public float alley { get; set; }
        public float extrahighetabove { get; set; }
        public float extrahighetbelow { get; set; }
        public uint decolimit { get; set; }
        public string comments { get; set; }
        public uint absolutedecolimit { get; set; }
        public uint housingdecolimitId { get; set; }
        public bool isSellable { get; set; }

    }
}
