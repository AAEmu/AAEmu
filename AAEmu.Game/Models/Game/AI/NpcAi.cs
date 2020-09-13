using System;
using System.Numerics;

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
using AAEmu.Game.Utils;

/*
   Author:Sagara, NLObP
*/
namespace AAEmu.Game.Models.Game.AI
{
    public sealed class NpcAi : ACreatureAi
    {
        public double Angle { get; set; }
        public float Distance { get; set; } = 0f;
        public float movingDistance { get; set; } = 0.27f;
        public double AngleTmp { get; set; }
        public float AngVelocity { get; set; } = 45.0f;
        public float MaxVelocityForward { get; set; } = 5.4f;
        public float MaxVelocityBackward { get; set; } = 0f;
        public float VelAccel { get; set; } = 1.8f;
        public Vector3 vMovingDistance { get; set; } = new Vector3();
        public Vector3 vMaxVelocityForwardRun { get; set; } = new Vector3(5.4f, 5.4f, 5.4f);
        public Vector3 vMaxVelocityForwardWalk { get; set; } = new Vector3(1.8f, 1.8f, 1.8f);
        public Vector3 Velocity { get; set; } = new Vector3();
        public Vector3 vMaxVelocityBackward { get; set; } = new Vector3();
        public Vector3 vVelAccel { get; set; } = new Vector3(1.8f, 1.8f, 1.8f);
        public Vector3 vPosition { get; set; } = new Vector3();
        public Vector3 vTarget { get; set; } = new Vector3();
        public Vector3 vDistance { get; set; } = new Vector3();
        public float RangeToCheckPoint { get; set; } = 1.0f; // distance to checkpoint at which it is considered that we have reached it
        public Vector3 vRangeToCheckPoint { get; set; } = new Vector3(1.0f, 1.0f, 0f); // distance to checkpoint at which it is considered that we have reached it

        public NpcAi(GameObject owner, float visibleRange) : base(owner, visibleRange)
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
                    // if the Npc is aggressive, he will look at us and attack if close to us, otherwise he just looks at us
                    if (!npc.IsInBattle && npc.Hp > 0)
                    {
                        if (npc.Faction.Id == 115 || npc.Faction.Id == 3) // npc.Faction.GuardHelp == false && 
                        {
                            if (npc.Template.Aggression && npc.Template.AggroLinkHelpDist > Math.Abs(MathUtil.CalculateDistance(npc.Position, chr.Position, true)))
                            {
                                // NPC attacking us
                                npc.Patrol = null;
                                // AiAggro(ai_commands = 4065, count=0)
                                chr.BroadcastPacket(new SCTargetChangedPacket(npc.ObjId, chr.ObjId), true);
                                chr.BroadcastPacket(new SCAiAggroPacket(npc.ObjId, 1, chr.ObjId), true);
                                chr.BroadcastPacket(new SCCombatEngagedPacket(npc.ObjId), true); // caster
                                chr.BroadcastPacket(new SCAggroTargetChangedPacket(npc.ObjId, chr.ObjId), true);
                                npc.CurrentTarget = target;
                                npc.SetForceAttack(true);
                                npc.IsAutoAttack = true;
                                npc.IsInBattle = true;
                                var combat = new Combat();
                                combat.Execute(npc);
                            }
                            else if (Math.Abs(MathUtil.CalculateDistance(npc.Position, chr.Position, true)) < 10f) // preferredCombatDistance = 20
                            {
                                //if (npc.Patrol == null || npc.Patrol.PauseAuto(npc))
                                {
                                    npc.Patrol = null;
                                    // Npc looks at us
                                    if (npc.CurrentTarget != target)
                                    {
                                        chr.BroadcastPacket(new SCTargetChangedPacket(npc.ObjId, chr.ObjId), true);
                                        npc.CurrentTarget = target;
                                    }
                                    var seq = (uint)(DateTime.Now - GameService.StartTime).TotalMilliseconds;
                                    var moveType = (ActorData)UnitMovement.GetType(UnitMovementType.Actor);

                                    moveType.X = npc.Position.X;
                                    moveType.Y = npc.Position.Y;
                                    moveType.Z = AppConfiguration.Instance.HeightMapsEnable ? WorldManager.Instance.GetHeight(npc.Position.ZoneId, npc.Position.X, npc.Position.Y) : npc.Position.Z;

                                    // looks in the direction of movement
                                    Angle = MathUtil.CalculateAngleFrom(npc, chr);
                                    var rotZ = MathUtil.ConvertDegreeToDirection(Angle);
                                    moveType.Rot = new Quaternion(0f, 0f, Helpers.ConvertDirectionToRadian(rotZ), 1f);
                                    npc.Rot = moveType.Rot;

                                    moveType.DeltaMovement = Vector3.Zero;
                                    Velocity = Vector3.Zero;

                                    moveType.Stance = 1;    //combat=0, idle=1
                                    moveType.Alertness = 1; //idle=0, alert = 1, combat=2
                                    moveType.Time = seq;
                                    chr.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);
                                }
                            }
                            else //if (npc.Faction.Id == 115 || npc.Faction.Id == 3) // npc.Faction.GuardHelp == false && 
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
                                        //// NPCs are moving squarely
                                        //var square = new Square { Interrupt = true, Loop = true, Abandon = false, Degree = 360 };
                                        //// (short)Rand.Next(180, 360); checking that they're not leaving their seats.
                                        //patrol = square;
                                        npc.Patrol = null;
                                    }
                                    else if (rnd > 500)
                                    {
                                        //// NPCs are moving around in a circle
                                        //patrol = new Circular { Interrupt = true, Loop = true, Abandon = false };
                                        npc.Patrol = null;
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
                                        //// NPC move along the weaving shuttle in the Y-axis.
                                        //var quill = new QuillY { Interrupt = true, Loop = true, Abandon = false, Degree = (short)Rand.Next(180, 360) };
                                        //patrol = quill;
                                        npc.Patrol = null;
                                    }
                                    else
                                    if (rnd > 200)
                                    {
                                        // NPC move along the weaving shuttle in the X-axis.
                                        var quill = new QuillX { Interrupt = true, Loop = true, Abandon = false, Degree = 360 };
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
                                        //npc.Patrol.LastPatrol = patrol;
                                        npc.Patrol.LastPatrol = null;

                                        patrol.Recovery(npc);
                                    }
                                }
                                else
                                {
                                    npc.Patrol.Recovery(npc);
                                }
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
            //switch (someone.UnitType)
            //{
            //    case BaseUnitType.Character:
            //        break;
            //    case BaseUnitType.Npc:
            //        break;
            //    case BaseUnitType.Slave:
            //        break;
            //    case BaseUnitType.Housing:
            //        break;
            //    case BaseUnitType.Transfer:
            //        break;
            //    case BaseUnitType.Mate:
            //        break;
            //    case BaseUnitType.Shipyard:
            //        break;
            //    default:
            //        throw new ArgumentOutOfRangeException();
            //}
        }

        protected override void SomeoneSeeMe(GameObject someone)
        {
            //switch (someone.UnitType)
            //{
            //    case BaseUnitType.Character:
            //        break;
            //    case BaseUnitType.Npc:
            //        break;
            //    case BaseUnitType.Slave:
            //        break;
            //    case BaseUnitType.Housing:
            //        break;
            //    case BaseUnitType.Transfer:
            //        break;
            //    case BaseUnitType.Mate:
            //        break;
            //    case BaseUnitType.Shipyard:
            //        break;
            //    default:
            //        throw new ArgumentOutOfRangeException();
            //}
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
                    // if the Npc is aggressive, he will look at us and attack if close to us, otherwise he just looks at us
                    if (!npc.IsInBattle && npc.Hp > 0)
                    {
                        if (npc.Faction.Id == 115 || npc.Faction.Id == 3) // npc.Faction.GuardHelp == false && 
                        {
                            if (npc.Template.Aggression && npc.Template.AggroLinkHelpDist > Math.Abs(MathUtil.CalculateDistance(npc.Position, chr.Position, true)))
                            {
                                // NPC attacking us
                                npc.Patrol = null;
                                // AiAggro(ai_commands = 4065, count=0)
                                chr.BroadcastPacket(new SCTargetChangedPacket(npc.ObjId, chr.ObjId), true);
                                chr.BroadcastPacket(new SCAiAggroPacket(npc.ObjId, 1, chr.ObjId), true);
                                chr.BroadcastPacket(new SCCombatEngagedPacket(npc.ObjId), true); // caster
                                chr.BroadcastPacket(new SCAggroTargetChangedPacket(npc.ObjId, chr.ObjId), true);
                                npc.CurrentTarget = target;
                                npc.SetForceAttack(true);
                                npc.IsAutoAttack = true;
                                npc.IsInBattle = true;
                                var combat = new Combat();
                                combat.Execute(npc);
                            }
                            else if (Math.Abs(MathUtil.CalculateDistance(npc.Position, chr.Position, true)) < 10f) // preferredCombatDistance = 20
                            {
                                //if (npc.Patrol == null || npc.Patrol.PauseAuto(npc))
                                {
                                    npc.Patrol = null;
                                    // Npc looks at us
                                    if (npc.CurrentTarget != target)
                                    {
                                        chr.BroadcastPacket(new SCTargetChangedPacket(npc.ObjId, chr.ObjId), true);
                                        npc.CurrentTarget = target;
                                    }
                                    var seq = (uint)(DateTime.Now - GameService.StartTime).TotalMilliseconds;
                                    var moveType = (ActorData)UnitMovement.GetType(UnitMovementType.Actor);

                                    moveType.X = npc.Position.X;
                                    moveType.Y = npc.Position.Y;
                                    moveType.Z = AppConfiguration.Instance.HeightMapsEnable ? WorldManager.Instance.GetHeight(npc.Position.ZoneId, npc.Position.X, npc.Position.Y) : npc.Position.Z;

                                    // looks in the direction of movement
                                    Angle = MathUtil.CalculateAngleFrom(npc, chr);
                                    var rotZ = MathUtil.ConvertDegreeToDirection(Angle);
                                    moveType.Rot = new Quaternion(0f, 0f, Helpers.ConvertDirectionToRadian(rotZ), 1f);
                                    npc.Rot = moveType.Rot;

                                    moveType.DeltaMovement = Vector3.Zero;
                                    Velocity = Vector3.Zero;

                                    moveType.Stance = 1;    //combat=0, idle=1
                                    moveType.Alertness = 1; //idle=0, alert = 1, combat=2
                                    moveType.Time = seq;
                                    chr.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);
                                }
                            }
                            else //if (npc.Faction.Id == 115 || npc.Faction.Id == 3) // npc.Faction.GuardHelp == false && 
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
                                        //// NPCs are moving squarely
                                        //var square = new Square { Interrupt = true, Loop = true, Abandon = false, Degree = 360 };
                                        //// (short)Rand.Next(180, 360); checking that they're not leaving their seats.
                                        //patrol = square;
                                        npc.Patrol = null;
                                    }
                                    else if (rnd > 500)
                                    {
                                        //// NPCs are moving around in a circle
                                        //patrol = new Circular { Interrupt = true, Loop = true, Abandon = false };
                                        npc.Patrol = null;
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
                                        //// NPC move along the weaving shuttle in the Y-axis.
                                        //var quill = new QuillY { Interrupt = true, Loop = true, Abandon = false, Degree = (short)Rand.Next(180, 360) };
                                        //patrol = quill;
                                        npc.Patrol = null;
                                    }
                                    else
                                    if (rnd > 200)
                                    {
                                        // NPC move along the weaving shuttle in the X-axis.
                                        var quill = new QuillX { Interrupt = true, Loop = true, Abandon = false, Degree = 360 };
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
                                        //npc.Patrol.LastPatrol = patrol;
                                        npc.Patrol.LastPatrol = null;

                                        patrol.Recovery(npc);
                                    }
                                }
                                else
                                {
                                    npc.Patrol.Recovery(npc);
                                }
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
            //switch (someone.UnitType)
            //{
            //    case BaseUnitType.Character:
            //        break;
            //    case BaseUnitType.Npc:
            //        break;
            //    case BaseUnitType.Slave:
            //        break;
            //    case BaseUnitType.Housing:
            //        break;
            //    case BaseUnitType.Transfer:
            //        break;
            //    case BaseUnitType.Mate:
            //        break;
            //    case BaseUnitType.Shipyard:
            //        break;
            //    default:
            //        throw new ArgumentOutOfRangeException();
            //}
        }
    }
}
