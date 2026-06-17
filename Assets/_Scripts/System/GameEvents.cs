using System;
using UnityEngine;

public static class GameEvents
{
    public static System.Action OnRoomEnterSuccess;
    public static Action<EntityPickedEvent> OnEntityPicked;
}
public readonly struct EntityPickedEvent
{
    public readonly long EntityId;

    public EntityPickedEvent(long entityId)
    {
        EntityId = entityId;
    }
}