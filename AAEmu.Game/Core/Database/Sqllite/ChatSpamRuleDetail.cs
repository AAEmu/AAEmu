using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ChatSpamRuleDetail
{
    public long? Id { get; set; }

    public long? ChatSpamRuleId { get; set; }

    public string Text { get; set; }

    public long? DetectedCaseNextDetailId { get; set; }

    public long? NotDetectedCaseNextDetailId { get; set; }

    public byte[] StartNode { get; set; }

    public byte[] EndNode { get; set; }
}
