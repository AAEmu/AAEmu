using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class QuestComponent
{
    public long? Id { get; set; }

    public long? QuestContextId { get; set; }

    public long? ComponentKindId { get; set; }

    public long? NextComponent { get; set; }

    public long? NpcAiId { get; set; }

    public long? NpcId { get; set; }

    public long? SkillId { get; set; }

    public byte[] SkillSelf { get; set; }

    public string AiPathName { get; set; }

    public long? AiPathTypeId { get; set; }

    public long? SoundId { get; set; }

    public long? NpcSpawnerId { get; set; }

    public byte[] PlayCinemaBeforeBubble { get; set; }

    public long? AiCommandSetId { get; set; }

    public byte[] OrUnitReqs { get; set; }

    public long? CinemaId { get; set; }

    public long? SummaryVoiceId { get; set; }

    public byte[] HideQuestMarker { get; set; }

    public long? BuffId { get; set; }
}
