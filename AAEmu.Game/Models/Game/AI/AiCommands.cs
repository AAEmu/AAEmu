using AAEmu.Game.Models.Game.AI.Static;

namespace AAEmu.Game.Models.Game.AI
{
    public class AiCommands
    {
        public uint Id { get; set; }
        public uint CmdSetId { get; set; }
        public AiCommandCategory CmdId { get; set; }
        public uint Param1 { get; set; }
        public string Param2 { get; set; }
    }
}
