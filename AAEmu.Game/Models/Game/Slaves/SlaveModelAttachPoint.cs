using System.Collections.Generic;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.Game.Models.Game.Slaves;

// ReSharper disable once ClassNeverInstantiated.Global
/// <summary>
/// Helper class for loading attachment points for slaves from json file
/// </summary>
public class SlaveModelAttachPoint
{
    public string Name { get; set; } // Not actually used in the server
    public uint ModelId { get; set; }
    public Dictionary<AttachPointKind, WorldSpawnPosition> AttachPoints { get; set; }
}
