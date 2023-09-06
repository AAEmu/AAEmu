using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class BubbleChat
{
    public long? Id { get; set; }

    public long? BubbleId { get; set; }

    public byte[] Start { get; set; }

    public long? Next { get; set; }

    public long? NpcId { get; set; }

    public long? DoodadId { get; set; }

    public long? NpcSpawnerId { get; set; }

    public long? Angle { get; set; }

    public long? KindId { get; set; }

    public long? SoundId { get; set; }

    public string Speech { get; set; }

    public string Facial { get; set; }

    public long? CameraId { get; set; }

    public string ChangeSpeakerName { get; set; }

    public long? VoiceId { get; set; }
}
