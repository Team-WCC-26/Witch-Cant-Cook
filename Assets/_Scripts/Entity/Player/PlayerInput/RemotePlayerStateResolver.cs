using Protocol;
using UnityEngine;

public sealed class RemotePlayerStateResolver : PlayerStateResolver
{
    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private bool hasRemoteTransform = false;

    private float positionLerpSpeed = 15f;
    private float rotationLerpSpeed = 15f;

    public RemotePlayerStateResolver(PlayerBrain brain) : base(brain)
    {
        targetPosition = brain.transform.position;
        targetRotation = brain.transform.rotation;

        if (brain.Rb != null)
        {
            brain.Rb.isKinematic = true;
            brain.Rb.useGravity = false;
            brain.Rb.linearVelocity = Vector3.zero;
            brain.Rb.angularVelocity = Vector3.zero;
        }
    }

    public override void UpdateTick()
    {
        if (!hasRemoteTransform)
        {
            return;
        }

        brain.transform.position = Vector3.Lerp(
            brain.transform.position,
            targetPosition,
            Time.deltaTime * positionLerpSpeed
        );

        brain.transform.rotation = Quaternion.Slerp(
            brain.transform.rotation,
            targetRotation,
            Time.deltaTime * rotationLerpSpeed
        );
    }

    public override void FixedTick()
    {
    }

    public override void NotifyCollision(Collision collision)
    {
    }

    public void ApplyRemotePacket(PlayerMovementPacket packet)
    {
        if (packet == null)
        {
            return;
        }

        if (packet.PlayerId != brain.PlayerId)
        {
            return;
        }

        ApplyRemoteState(
            ProtocolPlayerStateConverter.ToClientCombinedState(packet.CombinedState)
        );

        ApplyRemoteTransform(packet.Position, packet.Rotation);
    }

    public void ApplyRemoteState(PlayerCombinedState remoteState)
    {
        SetCurrentState(remoteState);
    }

    public void ApplyRemoteTransform(System.Numerics.Vector3 position, System.Numerics.Vector3 rotation)
    {
        targetPosition = ToUnityVector3(position);
        targetRotation = Quaternion.Euler(ToUnityVector3(rotation));
        hasRemoteTransform = true;
    }

    public void ApplyRemoteTransform(Vector3 position, Vector3 eulerAngles)
    {
        targetPosition = position;
        targetRotation = Quaternion.Euler(eulerAngles);
        hasRemoteTransform = true;
    }

    public void SetLerpSpeed(float positionSpeed, float rotationSpeed)
    {
        positionLerpSpeed = positionSpeed;
        rotationLerpSpeed = rotationSpeed;
    }

    private static Vector3 ToUnityVector3(System.Numerics.Vector3 value)
    {
        return new Vector3(value.X, value.Y, value.Z);
    }
}