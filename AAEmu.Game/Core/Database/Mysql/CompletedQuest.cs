using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Mysql;

/// <summary>
/// Quests marked as completed for character
/// </summary>
public partial class CompletedQuest
{
    public uint Id { get; set; }

    public byte[] Data { get; set; }

    public uint Owner { get; set; }
}
