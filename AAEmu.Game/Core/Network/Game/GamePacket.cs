using AAEmu.Commons.Cryptography;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Connections;

namespace AAEmu.Game.Core.Network.Game;

public abstract class GamePacket(ushort typeId, byte level) : PacketBase<GameConnection>(typeId)
{
    public byte Level { get; set; } = level;

    /// <summary>
    /// Set by a packet whose body is legitimately allowed to end before every declared field was
    /// read, so the client-to-game dispatch leaves <see cref="PacketStream.StrictReads"/> off for
    /// it. No client-to-game or proxy packet needs this today: the packets with a variable tail
    /// (the five that drain with <c>while (stream.HasBytes)</c> and the two that probe
    /// <see cref="PacketStream.LeftBytes"/>) all refuse the tail themselves when it is short, and
    /// a genuinely optional tail is the only thing that belongs here. A packet that overrides it
    /// owes a comment saying which bytes are optional and what a short body means.
    /// </summary>
    protected virtual bool TolerateTruncatedBody => false;

    /// <summary>
    /// The dispatch's view of the same decision, since it holds the packet as a base type rather
    /// than as a subclass.
    /// </summary>
    internal bool RequiresCompleteBody => !TolerateTruncatedBody;

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

            // The client's dispatcher reads the level from the first body byte and looks the signature up in
            // the level table. The signature value itself is never matched — dropping it (earlier mistake)
            // made the client eat the level byte as the signature and then treat the encrypted body's first
            // byte as the level, which is not a valid table entry, so the packet was dropped.
            var packet = new PacketStream()
                .Write((byte)0xdd)
                .Write(level);

            switch (level)
            {
                case 5:
                    // Encrypted message: StoCEncrypt( crc8 | SCMessageCount | TypeId | body ).
                    var count = EncryptionManager.Instance.NextSCMessageCount(Connection.Id, Connection.AccountId);
                    var bodyCrc = new PacketStream()
                        .Write(count)
                        .Write(TypeId)
                        .Write(this);
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
        switch (EffectiveLogLevel)
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
            switch (EffectiveLogLevel)
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
        catch (TruncatedPacketException ex)
        {
            // A client that cut its own frame short is expected traffic on a public connection, not
            // a server defect: warn, and keep the fatal path for a handler that is actually wrong.
            Logger.Warn("GamePacket: C->S type {0:X3} {1} body truncated: {2}",
                TypeId, ToString()?.Substring(23), ex.Message);
            throw;
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
