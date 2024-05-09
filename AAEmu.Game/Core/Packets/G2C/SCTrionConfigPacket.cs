using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCTrionConfigPacket : GamePacket
{
    private readonly bool _activate;
    private readonly string _platformUrl;
    private readonly string _commerceURLforSteam;
    private readonly string _commerceUrl;
    private readonly string _wikiURL;
    private readonly string _csUrl;

    public SCTrionConfigPacket(bool activate, string platformUrl, string commerceUrl, string commerceURLforSteam) : base(SCOffsets.SCTrionConfigPacket, 5)
    {
        _activate = activate;
        _platformUrl = platformUrl;
        _commerceURLforSteam = commerceURLforSteam;
        _commerceUrl = commerceUrl;
        _wikiURL = "";
        _csUrl = "";
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_activate);
        //stream.Write(_authUrl); // remove in AA 6070
        stream.Write(_platformUrl);
        stream.Write(_commerceUrl);
        stream.Write(_commerceURLforSteam);
        stream.Write(_wikiURL);
        stream.Write(_csUrl);
        return stream;
    }
}
