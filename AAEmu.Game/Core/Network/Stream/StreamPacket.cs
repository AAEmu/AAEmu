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
            _log.Fatal(ex);
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
                _log.Trace(logString);
                break;
            case PacketLogLevel.Debug:
                _log.Debug(logString);
                break;
            case PacketLogLevel.Info:
                _log.Info(logString);
                break;
            case PacketLogLevel.Warning:
                _log.Warn(logString);
                break;
            case PacketLogLevel.Error:
                _log.Error(logString);
                break;
            case PacketLogLevel.Fatal:
                _log.Fatal(logString);
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
                _log.Trace(logString);
                break;
            case PacketLogLevel.Debug:
                _log.Debug(logString);
                break;
            case PacketLogLevel.Info:
                _log.Info(logString);
                break;
            case PacketLogLevel.Warning:
                _log.Warn(logString);
                break;
            case PacketLogLevel.Error:
                _log.Error(logString);
                break;
            case PacketLogLevel.Fatal:
                _log.Fatal(logString);
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
            _log.Fatal(ex);
            throw;
        }

        return this;
    }
}
