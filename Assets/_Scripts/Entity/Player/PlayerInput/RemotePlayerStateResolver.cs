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

    public void ApplyRemoteState(PlayerCombinedState remoteState)
    {
        SetCurrentState(remoteState);
    }

    public void ApplyRemoteTransform(Vector3 position, Quaternion rotation)
    {
        targetPosition = position;
        targetRotation = rotation;
        hasRemoteTransform = true;
    }

    public void SetLerpSpeed(float positionSpeed, float rotationSpeed)
    {
        positionLerpSpeed = positionSpeed;
        rotationLerpSpeed = rotationSpeed;
    }
}