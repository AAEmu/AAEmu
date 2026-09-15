namespace AAEmu.Game.Models.Game.Skills.Buffs;

public class BuffEvents
{
    public EventHandler<OnBuffStartedArgs> OnBuffStarted = delegate { };
    public EventHandler<OnDispelledArgs> OnDispelled = delegate { };
    public EventHandler<OnTimeoutArgs> OnTimeout = delegate { };

    /// <summary>
    /// Raised just before a buff is exited because the buff its <c>buffs.require_buff_id</c> names
    /// went away. Fires before the exit, so this buff's own triggers are still subscribed.
    /// </summary>
    public EventHandler<OnRequiredBuffLostArgs> OnRequiredBuffLost = delegate { };

    /// <summary>Raised when the client asked for this buff to be cancelled (CSRemoveBuffPacket).</summary>
    public EventHandler<OnUserCancelArgs> OnUserCancel = delegate { };

    /// <summary>
    /// Raised for each stealth buff <c>Buffs.RemoveStealth</c> removes, before it exits, so the
    /// <c>remove_stealth</c> trigger still runs.
    /// </summary>
    public EventHandler<OnStealthRemovedArgs> OnStealthRemoved = delegate { };

    /// <summary>
    /// Raised when this buff's charge is used up absorbing damage: the last point of the shield has
    /// just been consumed (<c>Buff.ConsumeCharge</c>).
    /// </summary>
    public EventHandler<OnAbsorptionConsumedArgs> OnAbsorptionConsumed = delegate { };
}

public class OnBuffStartedArgs : EventArgs
{

}

public class OnDispelledArgs : EventArgs
{

}

public class OnTimeoutArgs : EventArgs
{

}

public class OnRequiredBuffLostArgs : EventArgs
{
    /// <summary>The buff that went away and this buff required.</summary>
    public uint RequiredBuffId { get; set; }
}

public class OnUserCancelArgs : EventArgs
{

}

public class OnStealthRemovedArgs : EventArgs
{

}

public class OnAbsorptionConsumedArgs : EventArgs
{
    /// <summary>Damage this consumption absorbed.</summary>
    public int Amount { get; set; }
}
