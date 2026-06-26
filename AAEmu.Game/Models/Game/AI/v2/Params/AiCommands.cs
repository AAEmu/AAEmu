using AAEmu.Game.Models.Game.AI.Enums;

namespace AAEmu.Game.Models.Game.AI.v2.Params;

public class AiCommands
{
    public uint Id { get; set; }
    public uint CmdSetId { get; set; } // cmd_set_id
    public AiCommandCategory CmdId { get; set; } // cmd_id
    public string Param1 { get; set; } // param1 (10.0.2.13: varchar(32), e.g. "3 sec"/"7.5"; parse on use)
    public string Param2 { get; set; } // param2
}
