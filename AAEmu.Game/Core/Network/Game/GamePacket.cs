using AAEmu.Commons.Cryptography;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Connections;

namespace AAEmu.Game.Core.Network.Game;

public abstract class GamePacket(ushort typeId, byte level) : PacketBase<GameConnection>(typeId)
{
    public byte Level { get; set; } = level;

    /// <summary>
    /// This is called in Encode after Read() in the case of GamePackets
    /// The purpose is to separate packet data from packet behavior
    /// </summary>
    public virtual void Execute() { }

    public override PacketStream Encode()
    {
        var ps = new PacketStream();
        try
        {
            // Once the encrypted game channel is negotiated (X2EnterWorldResponse sent), the client rejects
            // plain level-1 packets — auto-upgrade them to level 5 (StoC). Level-2 packets bypass that gate.
            var level = Level;
            if (level == 1 && Connection.EncryptionActive)
                level = 5;

            // S->C frame = [u16 len][sig=0xDD][level][body]. RUNTIME-VERIFIED (crynetwork.dll sub_39574230
            // via ScyllaHide debug): the client's framing CONSUMES the sig byte after the length and feeds
            // a2=[level][body] to the dispatcher, which reads a2[0]=level via byte_395CF090[level]. The sig
            // value itself is never looked up — dropping it (earlier mistake) made the client eat the level
            // byte as the sig and treat the encrypted body's first byte as the level → byte_395CF090[x]=0 → drop.
            var packet = new PacketStream()
                .Write((byte)0xdd)
                .Write(level);

            switch (level)
            {
                case 5:
                    // Encrypted message: StoCEncrypt( crc8 | SCMessageCount | TypeId | body ).
                    // StoC is the keyless length-seeded stream cipher (verified == server sub_39572530).
                    var count = EncryptionManager.Instance.GetSCMessageCount(Connection.Id, Connection.AccountId);
                    var bodyCrc = new PacketStream()
                        .Write(count)
                        .Write(TypeId)
                        .Write(this);
                    EncryptionManager.Instance.IncSCMsgCount(Connection.Id, Connection.AccountId);
                    var crc8 = EncryptionManager.Instance.Crc8(bodyCrc);
                    var data = new PacketStream()
                        .Write(crc8)
                        .Write(bodyCrc, false);
                    packet.Write(EncryptionManager.Instance.StoCEncrypt(data), false);
                    break;
                case 1:
                    packet
                        .Write((byte)0) // hash/crc (unused)
                        .Write((byte)0) // counter (unused)
                        .Write(TypeId)
                        .Write(this);
                    break;
                default: // level 2 and others: plaintext, no crc/counter
                    packet
                        .Write(TypeId)
                        .Write(this);
                    break;
            }

            ps.Write(packet);
        }
        catch (Exception ex)
        {
            Logger.Fatal(ex);
            throw;
        }

        var logString = $"GamePacket: S->C type {TypeId:X3} {ToString()?.Substring(23)}{Verbose()}";
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

    public override PacketBase<GameConnection> Decode(PacketStream ps)
    {
        try
        {
            Read(ps);

            var logString = $"GamePacket: C->S type {TypeId:X3} {ToString()?.Substring(23)}{Verbose()}";
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

            Execute();
        }
        catch (Exception ex)
        {
            Logger.Error("GamePacket: C->S type {0:X3} {1}", TypeId, ToString()?.Substring(23));
            Logger.Fatal(ex);
            throw;
        }

        return this;
    }
}
