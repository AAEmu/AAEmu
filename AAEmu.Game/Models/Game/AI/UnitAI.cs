using System;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.AI.Abstracts;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.Game.Units.Route;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Geo.Basic;
using AAEmu.Game.Utils;

/*
   Author:Sagara
*/
namespace AAEmu.Game.Models.Game.AI
{
    public sealed class UnitAi : ACreatureAi
    {
        public UnitAi(GameObject owner, float visibleRange) : base(owner, visibleRange)
        {
        }

        protected override void IamSeeSomeone(GameObject someone)
        {
            switch (someone.UnitType)
            {
                case BaseUnitType.Character:
                    var chr = (Character)someone;
                    var npc = (Npc)Owner;
                    var target = (BaseUnit)someone;
                    // если Npc агрессивный, то будет смотреть на нас и нападёт, иначе просто смотрит на нас
                    if (!npc.IsInBattle)
                    {
                        if (npc.Template.Aggression && npc.Template.AggroLinkHelpDist > Math.Abs(MathUtil.CalculateDistance(npc.Position, chr.Position, true)))
                        {
                            // NPC attacking us
                            //var square = new Square { Interrupt = true, Loop = true, Abandon = false, Degree = 360 };
                            //npc.Patrol = square;
                            //npc.Patrol.Pause(npc);
                            //npc.Patrol.LastPatrol = null;
                            //npc.Patrol.Recovery(npc);
                            // AiAggro(ai_commands = 4065, count=0)
                            chr.BroadcastPacket(new SCTargetChangedPacket(npc.ObjId, chr.ObjId), true);
                            chr.BroadcastPacket(new SCAiAggroPacket(npc.ObjId, 1, chr.ObjId), true);
                            chr.BroadcastPacket(new SCCombatEngagedPacket(npc.ObjId), true); // caster
                            //chr.BroadcastPacket(new SCCombatEngagedPacket(chr.ObjId), true); // target
                            //chr.BroadcastPacket(new SCCombatFirstHitPacket(chr.ObjId, npc.ObjId, 0), true);
                            chr.BroadcastPacket(new SCAggroTargetChangedPacket(npc.ObjId, chr.ObjId), true);
                            npc.CurrentTarget = target;
                            //TaskManager.Instance.Schedule(new UnitMove(new Track(), npc), TimeSpan.FromMilliseconds(1000));
                            npc.SetForceAttack(true);
                            npc.IsAutoAttack = true;
                            npc.IsInBattle = true;
                            var combat = new Combat();
                            combat.Execute(npc);
                        }
                        else if (Math.Abs(MathUtil.CalculateDistance(npc.Position, chr.Position, true)) < 20f)
                        {
                            // Stop NPCs
                            //npc.IsInPatrol = false;
                            //npc.Patrol = null;
                            npc.IsInBattle = false;

                            if (npc.Patrol == null || npc.Patrol.PauseAuto(npc))
                            {
                                // Npc looks at us
                                if (npc.CurrentTarget != target)
                                {
                                    chr.BroadcastPacket(new SCTargetChangedPacket(npc.ObjId, chr.ObjId), true);
                                    npc.CurrentTarget = target;
                                }
                                var seq = (uint)(DateTime.Now - GameService.StartTime).TotalMilliseconds;
                                var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);
                                moveType.X = npc.Position.X;
                                moveType.Y = npc.Position.Y;
                                moveType.Z = AppConfiguration.Instance.HeightMapsEnable ? WorldManager.Instance.GetHeight(npc.Position.ZoneId, npc.Position.X, npc.Position.Y) : npc.Position.Z;
                                var angle = MathUtil.CalculateAngleFrom(npc, chr);
                                var rotZ = MathUtil.ConvertDegreeToDirection(angle);
                                moveType.RotationZ = rotZ;
                                moveType.Flags = 5;
                                moveType.DeltaMovement = new sbyte[3];
                                moveType.DeltaMovement[0] = 0;
                                moveType.DeltaMovement[1] = 0;
                                moveType.DeltaMovement[2] = 0;
                                moveType.Stance = 0;    //combat=0, idle=1
                                moveType.Alertness = 2; //idle=0, combat=2
                                moveType.Time = seq;
                                chr.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);
                            }
                        }
                        else if (npc.Faction.GuardHelp == false)
                        {
                            if (npc.CurrentTarget != null)
                            {
                                chr.BroadcastPacket(new SCTargetChangedPacket(npc.ObjId, 0), true);
                                npc.CurrentTarget = null;
                            }
                            // here the NPCs can hunt, check that they are not protected by Guards
                            if (npc.Patrol == null)
                            {
                                npc.IsInBattle = false;
                                Patrol patrol = null;
                                var rnd = Rand.Next(0, 1000);
                                if (rnd > 700)
                                {
                                    // NPC stand still
                                    // turned it off because the NPCs are leaving their seats.
                                    npc.Patrol = null;
                                    // NPC is moving slowly
                                    //var stirring = new Stirring() { Interrupt = true, Loop = true, Abandon = false };
                                    //stirring.Degree = (short)Rand.Next(180, 360);
                                    //patrol = stirring;
                                }
                                else if (rnd > 600)
                                {
                                    // NPCs are moving squarely
                                    var square = new Square { Interrupt = true, Loop = true, Abandon = false, Degree = 360 };
                                    // (short)Rand.Next(180, 360); checking that they're not leaving their seats.
                                    patrol = square;
                                }
                                else if (rnd > 500)
                                {
                                    // NPCs are moving around in a circle
                                    patrol = new Circular { Interrupt = true, Loop = true, Abandon = false };
                                }
                                else if (rnd > 400)
                                {
                                    // NPC stand still
                                    // turned it off because the NPCs are leaving their seats.
                                    npc.Patrol = null;
                                    // NPCs are jerking around
                                    //var jerky = new Jerky { Interrupt = true, Loop = true, Abandon = false };
                                    //jerky.Degree = (short)Rand.Next(180, 360);
                                    //patrol = jerky;
                                }
                                else if (rnd > 300)
                                {
                                    // NPC move along the weaving shuttle in the Y-axis.
                                    var quill = new QuillY { Interrupt = true, Loop = true, Abandon = false, Degree = (short)Rand.Next(180, 360) };
                                    patrol = quill;
                                }
                                else if (rnd > 200)
                                {
                                    // NPC move along the weaving shuttle in the X-axis.
                                    var quill = new QuillX { Interrupt = true, Loop = true, Abandon = false, Degree = (short)Rand.Next(180, 360) };
                                    patrol = quill;
                                }
                                else
                                {
                                    // NPC stand still
                                    npc.Patrol = null;
                                }

                                if (patrol != null)
                                {
                                    patrol.Pause(npc);
                                    npc.Patrol = patrol;
                                    npc.Patrol.LastPatrol = patrol;
                                    patrol.Recovery(npc);
                                }
                            }
                            else
                            {
                                npc.Patrol.Recovery(npc);
                            }
                        }
                    }
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
            switch (someone.UnitType)
            {
                case BaseUnitType.Character:
                    // here the NPCs can hunt, check that they are not protected by Guards
                    //var npc = (Npc)Owner;
                    //if (npc.Faction.GuardHelp == false)
                    //{
                    //    if (npc.Patrol == null)
                    //    {
                    //        npc.IsInBattle = false;
                    //        Patrol patrol = null;
                    //        var rnd = Rand.Next(0, 1000);
                    //        if (rnd > 700)
                    //        {
                    //            // NPC stand still
                    //            // turned it off because the NPCs are leaving their seats.
                    //            npc.Patrol = null;
                    //            // NPC is moving slowly
                    //            //var stirring = new Stirring() { Interrupt = true, Loop = true, Abandon = false };
                    //            //stirring.Degree = (short)Rand.Next(180, 360);
                    //            //patrol = stirring;
                    //        }
                    //        else if (rnd > 600)
                    //        {
                    //            // NPCs are moving squarely
                    //            var square = new Square { Interrupt = true, Loop = true, Abandon = false, Degree = 360 };
                    //            // (short)Rand.Next(180, 360); checking that they're not leaving their seats.
                    //            patrol = square;
                    //        }
                    //        else if (rnd > 500)
                    //        {
                    //            // NPCs are moving around in a circle
                    //            patrol = new Circular { Interrupt = true, Loop = true, Abandon = false };
                    //        }
                    //        else if (rnd > 400)
                    //        {
                    //            // NPC stand still
                    //            // turned it off because the NPCs are leaving their seats.
                    //            npc.Patrol = null;
                    //            // NPCs are jerking around
                    //            //var jerky = new Jerky { Interrupt = true, Loop = true, Abandon = false };
                    //            //jerky.Degree = (short)Rand.Next(180, 360);
                    //            //patrol = jerky;
                    //        }
                    //        else if (rnd > 300)
                    //        {
                    //            // NPC move along the weaving shuttle in the Y-axis.
                    //            var quill = new QuillY { Interrupt = true, Loop = true, Abandon = false, Degree = (short)Rand.Next(180, 360) };
                    //            patrol = quill;
                    //        }
                    //        else if (rnd > 200)
                    //        {
                    //            // NPC move along the weaving shuttle in the X-axis.
                    //            var quill = new QuillX { Interrupt = true, Loop = true, Abandon = false, Degree = (short)Rand.Next(180, 360) };
                    //            patrol = quill;
                    //        }
                    //        else
                    //        {
                    //            // NPC stand still
                    //            npc.Patrol = null;
                    //        }

                    //        if (patrol != null)
                    //        {
                    //            patrol.Pause(npc);
                    //            npc.Patrol = patrol;
                    //            npc.Patrol.LastPatrol = patrol;
                    //            patrol.Recovery(npc);
                    //        }
                    //    }
                    //}
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

        protected override void SomeoneSeeMe(GameObject someone)
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

        protected override void SomeoneUnseeMee(GameObject someone)
        {
        }

        protected override void SomeoneThatIamSeeWasMoved(GameObject someone, MovementAction action)
        {
            switch (someone.UnitType)
            {
                case BaseUnitType.Character:
                    var chr = (Character)someone;
                    var npc = (Npc)Owner;
                    var target = (BaseUnit)someone;
                    // если Npc агрессивный, то будет смотреть на нас и нападёт, иначе просто смотрит на нас
                    if (!npc.IsInBattle)
                    {
                        if (npc.Template.Aggression && npc.Template.AggroLinkHelpDist > Math.Abs(MathUtil.CalculateDistance(npc.Position, chr.Position, true)))
                        {
                            // NPC attacking us
                            //var square = new Square { Interrupt = true, Loop = true, Abandon = false, Degree = 360 };
                            //npc.Patrol = square;
                            //npc.Patrol.Pause(npc);
                            //npc.Patrol.LastPatrol = null;
                            //npc.Patrol.Recovery(npc);
                            // AiAggro(ai_commands = 4065, count=0)
                            chr.BroadcastPacket(new SCTargetChangedPacket(npc.ObjId, chr.ObjId), true);
                            chr.BroadcastPacket(new SCAiAggroPacket(npc.ObjId, 1, chr.ObjId), true);
                            chr.BroadcastPacket(new SCCombatEngagedPacket(npc.ObjId), true); // caster
                            //chr.BroadcastPacket(new SCCombatEngagedPacket(chr.ObjId), true); // target
                            //chr.BroadcastPacket(new SCCombatFirstHitPacket(chr.ObjId, npc.ObjId, 0), true);
                            chr.BroadcastPacket(new SCAggroTargetChangedPacket(npc.ObjId, chr.ObjId), true);
                            npc.CurrentTarget = target;
                            //TaskManager.Instance.Schedule(new UnitMove(new Track(), npc), TimeSpan.FromMilliseconds(1000));
                            npc.SetForceAttack(true);
                            npc.IsAutoAttack = true;
                            npc.IsInBattle = true;
                            var combat = new Combat();
                            combat.Execute(npc);
                        }
                        else if (Math.Abs(MathUtil.CalculateDistance(npc.Position, chr.Position, true)) < 20f)
                        {
                            // Stop NPCs
                            //npc.IsInPatrol = false;
                            //npc.Patrol = null;
                            npc.IsInBattle = false;

                            if (npc.Patrol == null || npc.Patrol.PauseAuto(npc))
                            {
                                // Npc looks at us
                                if (npc.CurrentTarget != target)
                                {
                                    chr.BroadcastPacket(new SCTargetChangedPacket(npc.ObjId, chr.ObjId), true);
                                    npc.CurrentTarget = target;
                                }
                                var seq = (uint)(DateTime.Now - GameService.StartTime).TotalMilliseconds;
                                var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);
                                moveType.X = npc.Position.X;
                                moveType.Y = npc.Position.Y;
                                moveType.Z = AppConfiguration.Instance.HeightMapsEnable ? WorldManager.Instance.GetHeight(npc.Position.ZoneId, npc.Position.X, npc.Position.Y) : npc.Position.Z;
                                var angle = MathUtil.CalculateAngleFrom(npc, chr);
                                var rotZ = MathUtil.ConvertDegreeToDirection(angle);
                                moveType.RotationZ = rotZ;
                                moveType.Flags = 5;
                                moveType.DeltaMovement = new sbyte[3];
                                moveType.DeltaMovement[0] = 0;
                                moveType.DeltaMovement[1] = 0;
                                moveType.DeltaMovement[2] = 0;
                                moveType.Stance = 0;    //combat=0, idle=1
                                moveType.Alertness = 2; //idle=0, combat=2
                                moveType.Time = seq;
                                chr.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);
                            }
                        }
                        else if (npc.Faction.GuardHelp == false)
                        {
                            if (npc.CurrentTarget != null)
                            {
                                chr.BroadcastPacket(new SCTargetChangedPacket(npc.ObjId, 0), true);
                                npc.CurrentTarget = null;
                            }
                            // here the NPCs can hunt, check that they are not protected by Guards
                            if (npc.Patrol == null)
                            {
                                npc.IsInBattle = false;
                                Patrol patrol = null;
                                var rnd = Rand.Next(0, 1000);
                                if (rnd > 700)
                                {
                                    // NPC stand still
                                    // turned it off because the NPCs are leaving their seats.
                                    npc.Patrol = null;
                                    // NPC is moving slowly
                                    //var stirring = new Stirring() { Interrupt = true, Loop = true, Abandon = false };
                                    //stirring.Degree = (short)Rand.Next(180, 360);
                                    //patrol = stirring;
                                }
                                else if (rnd > 600)
                                {
                                    // NPCs are moving squarely
                                    var square = new Square { Interrupt = true, Loop = true, Abandon = false, Degree = 360 };
                                    // (short)Rand.Next(180, 360); checking that they're not leaving their seats.
                                    patrol = square;
                                }
                                else if (rnd > 500)
                                {
                                    // NPCs are moving around in a circle
                                    patrol = new Circular { Interrupt = true, Loop = true, Abandon = false };
                                }
                                else if (rnd > 400)
                                {
                                    // NPC stand still
                                    // turned it off because the NPCs are leaving their seats.
                                    npc.Patrol = null;
                                    // NPCs are jerking around
                                    //var jerky = new Jerky { Interrupt = true, Loop = true, Abandon = false };
                                    //jerky.Degree = (short)Rand.Next(180, 360);
                                    //patrol = jerky;
                                }
                                else if (rnd > 300)
                                {
                                    // NPC move along the weaving shuttle in the Y-axis.
                                    var quill = new QuillY { Interrupt = true, Loop = true, Abandon = false, Degree = (short)Rand.Next(180, 360) };
                                    patrol = quill;
                                }
                                else if (rnd > 200)
                                {
                                    // NPC move along the weaving shuttle in the X-axis.
                                    var quill = new QuillX { Interrupt = true, Loop = true, Abandon = false, Degree = (short)Rand.Next(180, 360) };
                                    patrol = quill;
                                }
                                else
                                {
                                    // NPC stand still
                                    npc.Patrol = null;
                                }

                                if (patrol != null)
                                {
                                    patrol.Pause(npc);
                                    npc.Patrol = patrol;
                                    npc.Patrol.LastPatrol = patrol;
                                    patrol.Recovery(npc);
                                }
                            }
                            else
                            {
                                npc.Patrol.Recovery(npc);
                            }
                        }
                    }
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
                    // here the NPCs can hunt, check that they are not protected by Guards
                    var npc = (Npc)Owner;
                    //if (npc.Faction.GuardHelp == false)
                    //{
                    //    if (npc.Patrol == null)
                    //    {
                    //        npc.IsInBattle = false;
                    //        Patrol patrol = null;
                    //        var rnd = Rand.Next(0, 1000);
                    //        if (rnd > 700)
                    //        {
                    //            // NPC stand still
                    //            // turned it off because the NPCs are leaving their seats.
                    //            npc.Patrol = null;
                    //            // NPC is moving slowly
                    //            //var stirring = new Stirring() { Interrupt = true, Loop = true, Abandon = false };
                    //            //stirring.Degree = (short)Rand.Next(180, 360);
                    //            //patrol = stirring;
                    //        }
                    //        else if (rnd > 600)
                    //        {
                    //            // NPCs are moving squarely
                    //            var square = new Square { Interrupt = true, Loop = true, Abandon = false, Degree = 360 };
                    //            // (short)Rand.Next(180, 360); checking that they're not leaving their seats.
                    //            patrol = square;
                    //        }
                    //        else if (rnd > 500)
                    //        {
                    //            // NPCs are moving around in a circle
                    //            patrol = new Circular { Interrupt = true, Loop = true, Abandon = false };
                    //        }
                    //        else if (rnd > 400)
                    //        {
                    //            // NPC stand still
                    //            // turned it off because the NPCs are leaving their seats.
                    //            npc.Patrol = null;
                    //            // NPCs are jerking around
                    //            //var jerky = new Jerky { Interrupt = true, Loop = true, Abandon = false };
                    //            //jerky.Degree = (short)Rand.Next(180, 360);
                    //            //patrol = jerky;
                    //        }
                    //        else if (rnd > 300)
                    //        {
                    //            // NPC move along the weaving shuttle in the Y-axis.
                    //            var quill = new QuillY { Interrupt = true, Loop = true, Abandon = false, Degree = (short)Rand.Next(180, 360) };
                    //            patrol = quill;
                    //        }
                    //        else if (rnd > 200)
                    //        {
                    //            // NPC move along the weaving shuttle in the X-axis.
                    //            var quill = new QuillX { Interrupt = true, Loop = true, Abandon = false, Degree = (short)Rand.Next(180, 360) };
                    //            patrol = quill;
                    //        }
                    //        else
                    //        {
                    //            // NPC stand still
                    //            npc.Patrol = null;
                    //        }

                    //        if (patrol != null)
                    //        {
                    //            patrol.Pause(npc);
                    //            npc.Patrol = patrol;
                    //            npc.Patrol.LastPatrol = patrol;
                    //            patrol.Recovery(npc);
                    //        }
                    //    }
                    //}
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
