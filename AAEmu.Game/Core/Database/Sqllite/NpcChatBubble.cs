using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class NpcChatBubble
{
    public long? Id { get; set; }

    public string Bubble { get; set; }

    public long? AiEventId { get; set; }
}
