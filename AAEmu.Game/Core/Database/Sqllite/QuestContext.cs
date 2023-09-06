using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class QuestContext
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public long? CategoryId { get; set; }

    public byte[] Repeatable { get; set; }

    public long? Level { get; set; }

    public byte[] Selective { get; set; }

    public byte[] Successive { get; set; }

    public byte[] RestartOnFail { get; set; }

    public long? ChapterIdx { get; set; }

    public long? QuestIdx { get; set; }

    public long? MilestoneId { get; set; }

    public byte[] LetItDone { get; set; }

    public long? DetailId { get; set; }

    public long? ZoneId { get; set; }

    public long? Degree { get; set; }

    public byte[] UseQuestCamera { get; set; }

    public long? Score { get; set; }

    public byte[] UseAcceptMessage { get; set; }

    public byte[] UseCompleteMessage { get; set; }

    public long? GradeId { get; set; }

    public byte[] Translate { get; set; }
}
