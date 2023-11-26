using System;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game;

public class TelescopeRegistrationEntry
{
    private float _showPublicTransportRange;
    private float _showFishSchoolRange;
    private float _showShipTelescopeRange;
    public Character Player { get; set; }

    public float ShowPublicTransportRange
    {
        get => _showPublicTransportRange;
        set
        {
            if (Math.Abs(_showPublicTransportRange - value) < 1f)
                return;
            _showPublicTransportRange = value;
            Player?.SendPacket(new SCTransferTelescopeToggledPacket(_showPublicTransportRange > 0, _showPublicTransportRange));
        }
    }

    public float ShowFishSchoolRange
    {
        get => _showFishSchoolRange;
        set
        {
            if (Math.Abs(_showFishSchoolRange - value) < 1f)
                return;
            _showFishSchoolRange = value;
            Player?.SendPacket(new SCSchoolOfFishFinderToggledPacket(_showFishSchoolRange > 0, _showFishSchoolRange));
        }
    }

    public float ShowShipTelescopeRange
    {
        get => _showShipTelescopeRange;
        set
        {
            if (Math.Abs(_showShipTelescopeRange - value) < 1f)
                return;
            _showShipTelescopeRange = value;
            // TODO: Implement Ship radar
            Player?.SendPacket(new SCTelescopeToggledPacket(_showShipTelescopeRange > 0, _showShipTelescopeRange));
        }
    }

    public bool IsActive => ShowPublicTransportRange > 0 || ShowFishSchoolRange > 0 || ShowShipTelescopeRange > 0;
}
