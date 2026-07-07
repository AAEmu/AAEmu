using System.Numerics;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.Crime;

public class BountyDescription
{
    public uint PlayerId { get; set; }
    public string PlayerName { get; set; }
    public byte PlayerLevel { get; set; }
    public uint ZoneId { get; set; } // Zone key?
    public Vector3 Pos { get; set; }
    public ulong BountyAmount { get; set; }
    public short CrimePoints { get; set; }
    public bool IsLive { get; set; }
    public Race PlayerRace { get; set; }
    public int Type184 { get; set; }
    public int ArrestCount { get; set; }
    public int AcceptGuiltyCount { get; set; }
    public int AcceptTrialCount { get; set; }
    public int GuiltyCount { get; set; }
    public int NotGuiltyCount { get; set; }
    public int Seconds { get; set; }
}

/*
int __thiscall sub_397BEBF0(int this, struc_2 *a2)
{
  // [COLLAPSED LOCAL DECLARATIONS. PRESS NUMPAD "+" TO EXPAND]

  (a2->Read->UInt32)(a2, "type", this, 0);
  if ( (a2->Read->len)(a2) )
  {
    *((a2->Read->String)(a2, "name", this + 4, 128) + this + 4) = 0;
  }
  else
  {
    v3 = XlStringSize((this + 4));
    (a2->Read->String1)(a2, "name", this + 4, v3);
  }
  
  (a2->Read->Byte)(a2, "level", this + 133, 0);
  (a2->Read->Int32)(a2, "zoneId", this + 136, 0);
  (a2->Read->Int64)(a2, "posX", this + 144, 0);
  (a2->Read->Int64)(a2, "posY", this + 152, 0);
  (a2->Read->Float)(a2, "posZ", this + 160, 0);
  (a2->Read->UInt64)(a2, "bounty", this + 168, 0);
  a2->Read->Int16(a2, "crimePoint", (this + 176), 0);
  (a2->Read->Bool)(a2, "isLive", this + 178, 0);
  if ( a2->Read->len() )
  {
    (a2->Read->Byte)(a2, "CharRace", &v6);
    *(this + 180) = v6;
  }
  else
  {
    Byte = a2->Read->Byte;
    v6 = *(this + 180);
    Byte(a2, "CharRace", &v6);
  }
  (a2->Read->UInt32)(a2, "type", this + 184, 0);
  (a2->Read->Int32)(a2, "arrest", this + 188, 0);
  (a2->Read->Int32)(a2, "acceptGuilty", this + 192, 0);
  (a2->Read->Int32)(a2, "acceptTrial", this + 196, 0);
  (a2->Read->Int32)(a2, "notGuilty", this + 200, 0);
  (a2->Read->Int32)(a2, "guilty", this + 204, 0);
  return (a2->Read->Int32)(a2, "seconds", this + 208, 0);
}
*/
