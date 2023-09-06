using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class QuestActCheckTimer
{
    public long? Id { get; set; }

    public long? LimitTime { get; set; }

    public byte[] ForceChangeComponent { get; set; }

    public long? NextComponent { get; set; }

    public byte[] PlaySkill { get; set; }

    public long? SkillId { get; set; }

    public byte[] CheckBuf { get; set; }

    public long? BuffId { get; set; }

    public byte[] SustainBuf { get; set; }

    public long? TimerNpcId { get; set; }

    public byte[] IsSkillPlayer { get; set; }
}
