using System.Collections.Generic;

namespace AAEmu.Game.IO
{
    public class ClientDataConfig
    {
        public List<string> Sources { get; set; } = new List<string>();
        public bool PreferClientHeightMap { get; set; } = true;
    }
}
