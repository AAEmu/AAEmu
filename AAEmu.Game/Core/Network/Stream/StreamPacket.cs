using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.S2C;

namespace AAEmu.Game.Core.Network.Stream;

public abstract class StreamPacket : PacketBase<StreamConnection>
{
    protected StreamPacket(ushort typeId) : base(typeId)
    {
    }

    public override PacketStream Encode()
    {
        var ps = new PacketStream();
        try
        {
            ps.Write(new PacketStream().Write(TypeId).Write(this));
        }
        catch (Exception ex)
        {
            Logger.Fatal(ex);
            throw;
        }

        string logString;
        if ((TypeId == TCOffsets.TCDoodadIdsPacket) || ((TypeId == TCOffsets.TCDoodadStreamPacket)))
            logString = $"StreamPacket: S->C type {TypeId:X3} {ToString()?.Substring(23)}{Verbose()}";
        else
            logString = $"StreamPacket: S->C {ToString()?.Substring(23)}\n{ps}";

        switch (LogLevel)
        {
            case PacketLogLevel.Trace:
                Logger.Trace(logString);
                break;
            case PacketLogLevel.Debug:
                Logger.Debug(logString);
                break;
            case PacketLogLevel.Info:
                Logger.Info(logString);
                break;
            case PacketLogLevel.Warning:
                Logger.Warn(logString);
                break;
            case PacketLogLevel.Error:
                Logger.Error(logString);
                break;
            case PacketLogLevel.Fatal:
                Logger.Fatal(logString);
                break;
            case PacketLogLevel.Off:
            default:
                break;
        }

        return ps;
    }

    public override PacketBase<StreamConnection> Decode(PacketStream ps)
    {
        var logString = $"StreamPacket: C->S type {TypeId:X3} {ToString()?.Substring(23)}{Verbose()}\n{ps}";
        switch (LogLevel)
        {
            case PacketLogLevel.Trace:
                Logger.Trace(logString);
                break;
            case PacketLogLevel.Debug:
                Logger.Debug(logString);
                break;
            case PacketLogLevel.Info:
                Logger.Info(logString);
                break;
            case PacketLogLevel.Warning:
                Logger.Warn(logString);
                break;
            case PacketLogLevel.Error:
                Logger.Error(logString);
                break;
            case PacketLogLevel.Fatal:
                Logger.Fatal(logString);
                break;
            case PacketLogLevel.Off:
            default:
                break;
        }

        try
        {
            Read(ps);
        }
        catch (Exception ex)
        {
            Logger.Fatal(ex);
            throw;
        }

        return this;
    }
}
