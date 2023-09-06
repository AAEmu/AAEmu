using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Mysql;

/// <summary>
/// Currently open quests
/// </summary>
public partial class Quest
{
    public uint Id { get; set; }

    public uint TemplateId { get; set; }

    public byte[] Data { get; set; }

    public sbyte Status { get; set; }

    public uint Owner { get; set; }
}
