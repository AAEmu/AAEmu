﻿using System;

using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

public abstract class ItemTask : PacketMarshaler
{
    protected ItemAction _type;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)_type);
        return stream;
    }

    public virtual void ReadDetails(PacketStream stream)
    {
    }

    protected virtual void WriteDetails(PacketStream stream, Item item)
    {
        if (item == null)
        {
            throw new ArgumentNullException(nameof(item));
        }

        stream.Write(item.TemplateId);      // type
        stream.Write(item.Id);              // id
        stream.Write(item.Grade);           // type
        stream.Write((byte)item.ItemFlags); // bounded
        stream.Write(item.Count);           // stack

        var details = new PacketStream();
        details.Write((byte)item.DetailType);
        item.WriteDetails(details);

        stream.Write((short)128);        // length details
        stream.Write(details, false);
        stream.Write(new byte[128 - details.Count]);

        stream.Write(item.CreateTime);   // creationTime
        stream.Write(item.LifespanMins); // lifespanMins
        stream.Write(item.MadeUnitId);   // type
        stream.Write(item.WorldId);      // worldId
        stream.Write(item.UnsecureTime); // unsecureDateTime
        stream.Write(item.UnpackTime);   // unpackDateTime
    }
}
