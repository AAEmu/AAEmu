using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class QuestChatBubble
{
    public long? Id { get; set; }

    public long? QuestComponentId { get; set; }

    public string Speech { get; set; }

    public long? NpcId { get; set; }

    public long? NextBubble { get; set; }

    public byte[] IsStart { get; set; }

    public long? SoundId { get; set; }

    public long? NpcSpawnerId { get; set; }

    public long? Angle { get; set; }

    public long? ChatBubbleKindId { get; set; }

    public string Facial { get; set; }

    public long? NpcGroupId { get; set; }

    public long? CameraId { get; set; }

    public string ChangeSpeakerName { get; set; }
}
