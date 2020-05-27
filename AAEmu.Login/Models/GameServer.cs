﻿namespace AAEmu.Login.Models
{
    public class GameServer
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
        public bool Hidden { get; set; }
        public Types Type { get; set; }
        public Colors Color { get; set; }
        
        public enum Types
        {
            None = 0,
            Fresh = 1,
            Evo = 2,
            War = 3,
            Unk4 = 4
        }
    
        public enum Colors
        {
            Blue = 0,
            Green = 1,
            Purple = 2,
            Red = 8
        }
    }
}
