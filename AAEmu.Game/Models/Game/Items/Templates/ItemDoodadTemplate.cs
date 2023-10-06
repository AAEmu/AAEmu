using System.Collections.Generic;

namespace AAEmu.Game.Models.Game.Items.Templates;

public class ItemDoodadTemplate
{
    public uint DoodadId { get; set; }
    public List<uint> ItemIds { get; set; }
}
