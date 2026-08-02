namespace AAEmu.Game.Core.Managers.Id;

/// <summary>
/// Broadcast ObjIds for entities that do <b>not</b> index the dedicate unit table
/// (doodads, gimmicks). Starts at <see cref="ObjectIdManager.DedicateMaxUnitExclusive"/>
/// so they never collide with unit bcIds and never AV Zone Create.
/// </summary>
public interface INonUnitObjectIdManager : IIdManager;
