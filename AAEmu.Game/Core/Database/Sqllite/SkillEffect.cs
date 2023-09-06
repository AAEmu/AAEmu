using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class SkillEffect
{
    public long? Id { get; set; }

    public long? SkillId { get; set; }

    public long? EffectId { get; set; }

    public long? Weight { get; set; }

    public long? StartLevel { get; set; }

    public long? EndLevel { get; set; }

    public byte[] Friendly { get; set; }

    public byte[] NonFriendly { get; set; }

    public long? TargetBuffTagId { get; set; }

    public long? TargetNobuffTagId { get; set; }

    public long? SourceBuffTagId { get; set; }

    public long? SourceNobuffTagId { get; set; }

    public long? Chance { get; set; }

    public byte[] Front { get; set; }

    public byte[] Back { get; set; }

    public long? TargetNpcTagId { get; set; }

    public long? ApplicationMethodId { get; set; }

    public byte[] SynergyText { get; set; }

    public byte[] ConsumeSourceItem { get; set; }

    public long? ConsumeItemId { get; set; }

    public long? ConsumeItemCount { get; set; }

    public byte[] AlwaysHit { get; set; }

    public long? ItemSetId { get; set; }

    public byte[] InteractionSuccessHit { get; set; }
}
