using UnityEngine;

public static class ProtocolPlayerStateConverter
{
    public static PlayerCombinedState ToClientCombinedState(Protocol.PlayerCombinedState state)
    {
        return new PlayerCombinedState(
            ToClientPhysicalMode(state.PhysicalMode),
            ToUnityVector2(state.MoveDir),
            state.IsRun,
            ToClientInteraction(state.Interaction),
            //TODO : Protocol.PlayerCombinedState에 HeldObjType 필드 추가 후 패킷 값을 변환하도록 수정 필요
            CatchableObjType.Default
        );
    }

    public static PlayerPhysicalMode ToClientPhysicalMode(Protocol.PlayerPhysicalMode mode)
    {
        return mode switch
        {
            Protocol.PlayerPhysicalMode.Default => PlayerPhysicalMode.Default,
            Protocol.PlayerPhysicalMode.Ragdoll => PlayerPhysicalMode.Ragdoll,
            Protocol.PlayerPhysicalMode.Recover => PlayerPhysicalMode.Recover,
            _ => PlayerPhysicalMode.Default
        };
    }

    public static PlayerInteraction ToClientInteraction(Protocol.PlayerInteraction interaction)
    {
        return interaction switch
        {
            Protocol.PlayerInteraction.None => PlayerInteraction.None,
            Protocol.PlayerInteraction.Pick => PlayerInteraction.Pick,
            Protocol.PlayerInteraction.Drop => PlayerInteraction.Drop,
            Protocol.PlayerInteraction.Throw => PlayerInteraction.Throw,
            Protocol.PlayerInteraction.Use => PlayerInteraction.Use,
            Protocol.PlayerInteraction.SpecialInteract => PlayerInteraction.SpecialInteract,
            _ => PlayerInteraction.None
        };
    }

    public static Vector2 ToUnityVector2(System.Numerics.Vector2 value)
    {
        return new Vector2(value.X, value.Y);
    }

    public static Vector3 ToUnityVector3(System.Numerics.Vector3 value)
    {
        return new Vector3(value.X, value.Y, value.Z);
    }

    public static Protocol.PlayerCombinedState ToProtocolCombinedState(PlayerCombinedState state)
    {
        return new Protocol.PlayerCombinedState(
            ToProtocolPhysicalMode(state.PhysicalMode),
            ToNumericsVector2(state.MoveDir),
            state.IsRun,
            ToProtocolInteraction(state.Interaction)
            //TODO : Protocol.PlayerCombinedState 생성자에 HeldObjType 인자 추가 후 state.HeldObjType 전달 필요
        );
    }

    public static Protocol.PlayerPhysicalMode ToProtocolPhysicalMode(PlayerPhysicalMode mode)
    {
        return mode switch
        {
            PlayerPhysicalMode.Default => Protocol.PlayerPhysicalMode.Default,
            PlayerPhysicalMode.Ragdoll => Protocol.PlayerPhysicalMode.Ragdoll,
            PlayerPhysicalMode.Recover => Protocol.PlayerPhysicalMode.Recover,
            _ => Protocol.PlayerPhysicalMode.Default
        };
    }

    public static Protocol.PlayerInteraction ToProtocolInteraction(PlayerInteraction interaction)
    {
        return interaction switch
        {
            PlayerInteraction.None => Protocol.PlayerInteraction.None,
            PlayerInteraction.Pick => Protocol.PlayerInteraction.Pick,
            PlayerInteraction.Drop => Protocol.PlayerInteraction.Drop,
            PlayerInteraction.Throw => Protocol.PlayerInteraction.Throw,
            PlayerInteraction.Use => Protocol.PlayerInteraction.Use,
            PlayerInteraction.SpecialInteract => Protocol.PlayerInteraction.SpecialInteract,
            _ => Protocol.PlayerInteraction.None
        };
    }

    public static System.Numerics.Vector2 ToNumericsVector2(Vector2 value)
    {
        return new System.Numerics.Vector2(value.x, value.y);
    }

    public static System.Numerics.Vector3 ToNumericsVector3(Vector3 value)
    {
        return new System.Numerics.Vector3(value.x, value.y, value.z);
    }
}
