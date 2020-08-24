using System;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.AI.Abstracts;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Gimmicks;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Route;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Geo.Basic;

/*
   Author:Sagara, NLObP
*/
namespace AAEmu.Game.Models.Game.AI
{
    public sealed class GimmickAi : ACreatureAi
    {
        public GimmickAi(GameObject owner, float visibleRange) : base(owner, visibleRange)
        {
        }

        protected override void IamSeeSomeone(GameObject someone)
        {
            Patrol patrol;
            switch (someone.UnitType)
            {
                case BaseUnitType.Character:
                    //var chr = (Character)someone;
                    var gimmick = (Gimmick)Owner;
                    //var target = (BaseUnit)someone;
                    // Gimmick move along the weaving shuttle in the Z-axis.
                    if (gimmick.Patrol != null) { return; }
                    var quill = new QuillZ { Interrupt = false, Loop = true, Abandon = false, Degree = (short)Rand.Next(180, 360) };
                    patrol = quill;
                    patrol.Pause(gimmick);
                    gimmick.Patrol = patrol;
                    gimmick.Patrol.LastPatrol = patrol;
                    patrol.Recovery(gimmick);
                    break;
                case BaseUnitType.Npc:
                    break;
                case BaseUnitType.Slave:
                    break;
                case BaseUnitType.Housing:
                    break;
                case BaseUnitType.Transfer:
                    break;
                case BaseUnitType.Mate:
                    break;
                case BaseUnitType.Shipyard:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        protected override void IamUnseeSomeone(GameObject someone)
        {
        }

        protected override void SomeoneSeeMe(GameObject someone)
        {
            Patrol patrol;
            switch (someone.UnitType)
            {
                case BaseUnitType.Character:
                    //var chr = (Character)someone;
                    var gimmick = (Gimmick)Owner;
                    //var target = (BaseUnit)someone;
                    // Gimmick move along the weaving shuttle in the Z-axis.
                    if (gimmick.Patrol != null) { return; }
                    var quill = new QuillZ { Interrupt = false, Loop = true, Abandon = false, Degree = (short)Rand.Next(180, 360) };
                    patrol = quill;
                    patrol.Pause(gimmick);
                    gimmick.Patrol = patrol;
                    gimmick.Patrol.LastPatrol = patrol;
                    patrol.Recovery(gimmick);
                    break;
                case BaseUnitType.Npc:
                    break;
                case BaseUnitType.Slave:
                    break;
                case BaseUnitType.Housing:
                    break;
                case BaseUnitType.Transfer:
                    break;
                case BaseUnitType.Mate:
                    break;
                case BaseUnitType.Shipyard:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        protected override void SomeoneUnseeMee(GameObject someone)
        {
            switch (someone.UnitType)
            {
                case BaseUnitType.Character:
                    break;
                case BaseUnitType.Npc:
                    break;
                case BaseUnitType.Slave:
                    break;
                case BaseUnitType.Housing:
                    break;
                case BaseUnitType.Transfer:
                    break;
                case BaseUnitType.Mate:
                    break;
                case BaseUnitType.Shipyard:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        protected override void SomeoneThatIamSeeWasMoved(GameObject someone, MovementAction action)
        {
            switch (someone.UnitType)
            {
                case BaseUnitType.Character:
                    break;
                case BaseUnitType.Npc:
                    break;
                case BaseUnitType.Slave:
                    break;
                case BaseUnitType.Housing:
                    break;
                case BaseUnitType.Transfer:
                    break;
                case BaseUnitType.Mate:
                    break;
                case BaseUnitType.Shipyard:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        protected override void SomeoneThatSeeMeWasMoved(GameObject someone, MovementAction action)
        {
            switch (someone.UnitType)
            {
                case BaseUnitType.Character:
                    break;
                case BaseUnitType.Npc:
                    break;
                case BaseUnitType.Slave:
                    break;
                case BaseUnitType.Housing:
                    break;
                case BaseUnitType.Transfer:
                    break;
                case BaseUnitType.Mate:
                    break;
                case BaseUnitType.Shipyard:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}
