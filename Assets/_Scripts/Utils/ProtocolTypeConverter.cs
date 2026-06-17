using UnityEngine;

public static class ProtocolTypeConverter
{
    #region Player State
    public static PlayerCombinedState ToClientCombinedState(Protocol.PlayerCombinedState state)
    {
        return new PlayerCombinedState(
            ToClientPhysicalMode(state.PhysicalMode),
            ToUnityVector2(state.MoveDir),
            state.IsRun,
            ToClientInteraction(state.Interaction),
            ToClientCatchableObjType(state.HeldObjType)
        );
    }

    public static Protocol.PlayerCombinedState ToProtocolCombinedState(PlayerCombinedState state)
    {
        return new Protocol.PlayerCombinedState(
            ToProtocolPhysicalMode(state.PhysicalMode),
            ToNumericsVector2(state.MoveDir),
            state.IsRun,
            ToProtocolInteraction(state.Interaction),
            ToProtocolCatchableObjType(state.HeldObjType)
        );
    }

    private static PlayerPhysicalMode ToClientPhysicalMode(Protocol.PlayerPhysicalMode mode)
    {
        return mode switch
        {
            Protocol.PlayerPhysicalMode.Default => PlayerPhysicalMode.Default,
            Protocol.PlayerPhysicalMode.Ragdoll => PlayerPhysicalMode.Ragdoll,
            Protocol.PlayerPhysicalMode.Recover => PlayerPhysicalMode.Recover,
            _ => PlayerPhysicalMode.Default
        };
    }

    private static Protocol.PlayerPhysicalMode ToProtocolPhysicalMode(PlayerPhysicalMode mode)
    {
        return mode switch
        {
            PlayerPhysicalMode.Default => Protocol.PlayerPhysicalMode.Default,
            PlayerPhysicalMode.Ragdoll => Protocol.PlayerPhysicalMode.Ragdoll,
            PlayerPhysicalMode.Recover => Protocol.PlayerPhysicalMode.Recover,
            _ => Protocol.PlayerPhysicalMode.Default
        };
    }

    private static PlayerInteraction ToClientInteraction(Protocol.PlayerInteraction interaction)
    {
        return interaction switch
        {
            Protocol.PlayerInteraction.None => PlayerInteraction.None,
            Protocol.PlayerInteraction.Pick => PlayerInteraction.DefaultPrimary,
            Protocol.PlayerInteraction.Drop => PlayerInteraction.HeldPrimary,
            Protocol.PlayerInteraction.Throw => PlayerInteraction.Secondary,
            Protocol.PlayerInteraction.Use => PlayerInteraction.KeyInteract,
            Protocol.PlayerInteraction.SpecialInteract => PlayerInteraction.SpecialInteract,
            _ => PlayerInteraction.None
        };
    }

    private static Protocol.PlayerInteraction ToProtocolInteraction(PlayerInteraction interaction)
    {
        return interaction switch
        {
            PlayerInteraction.None => Protocol.PlayerInteraction.None,
            PlayerInteraction.DefaultPrimary => Protocol.PlayerInteraction.Pick,
            PlayerInteraction.HeldPrimary => Protocol.PlayerInteraction.Drop,
            PlayerInteraction.Secondary => Protocol.PlayerInteraction.Throw,
            PlayerInteraction.KeyInteract => Protocol.PlayerInteraction.Use,
            PlayerInteraction.SpecialInteract => Protocol.PlayerInteraction.SpecialInteract,
            _ => Protocol.PlayerInteraction.None
        };
    }

    private static CatchableObjType ToClientCatchableObjType(Protocol.CatchableObjType objType)
    {
        return objType switch
        {
            Protocol.CatchableObjType.Default => CatchableObjType.Default,
            Protocol.CatchableObjType.Ingredient => CatchableObjType.Ingredient,
            Protocol.CatchableObjType.Plate => CatchableObjType.Plate,
            Protocol.CatchableObjType.Knife => CatchableObjType.Knife,
            Protocol.CatchableObjType.Pan => CatchableObjType.Pan,
            Protocol.CatchableObjType.Broom => CatchableObjType.Broom,
            Protocol.CatchableObjType.Bucket => CatchableObjType.Bucket,
            _ => CatchableObjType.Default
        };
    }

    private static Protocol.CatchableObjType ToProtocolCatchableObjType(CatchableObjType objType)
    {
        return objType switch
        {
            CatchableObjType.Default => Protocol.CatchableObjType.Default,
            CatchableObjType.Ingredient => Protocol.CatchableObjType.Ingredient,
            CatchableObjType.Plate => Protocol.CatchableObjType.Plate,
            CatchableObjType.Knife => Protocol.CatchableObjType.Knife,
            CatchableObjType.Pan => Protocol.CatchableObjType.Pan,
            CatchableObjType.Broom => Protocol.CatchableObjType.Broom,
            CatchableObjType.Bucket => Protocol.CatchableObjType.Bucket,
            _ => Protocol.CatchableObjType.Default
        };
    }
    #endregion

    #region Vector
    public static Vector2 ToUnityVector2(System.Numerics.Vector2 value)
    {
        return new Vector2(value.X, value.Y);
    }

    public static Vector3 ToUnityVector3(System.Numerics.Vector3 value)
    {
        return new Vector3(value.X, value.Y, value.Z);
    }

    public static System.Numerics.Vector2 ToNumericsVector2(Vector2 value)
    {
        return new System.Numerics.Vector2(value.x, value.y);
    }

    public static System.Numerics.Vector3 ToNumericsVector3(Vector3 value)
    {
        return new System.Numerics.Vector3(value.x, value.y, value.z);
    }

    public static System.Numerics.Quaternion ToNumericsQuaternion(Quaternion value)
    {
        return new System.Numerics.Quaternion(value.x, value.y, value.z, value.w);
    }
    #endregion
}
