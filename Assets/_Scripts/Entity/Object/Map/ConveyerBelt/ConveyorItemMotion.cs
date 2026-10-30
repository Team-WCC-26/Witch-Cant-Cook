using UnityEngine;

[System.Serializable]
public sealed class ConveyorItemAnimationSettings
{
    [InspectorName("애니메이션 사용")] public bool enabled = true;
    [InspectorName("상하 움직임 높이"), Min(0f)] public float bobHeight = 0.06f;
    [InspectorName("상하 움직임 속도"), Min(0f)] public float bobFrequency = 0.65f;
    [InspectorName("초당 회전 각도")] public float rotationSpeed = 18f;
    [InspectorName("기울기 각도"), Min(0f)] public float tiltAngle = 4f;
    [InspectorName("기울기 속도"), Min(0f)] public float tiltFrequency = 0.5f;
    [InspectorName("시작 전환 시간"), Min(0f)] public float blendInDuration = 0.25f;
}

/// <summary>Owns belt physics temporarily; animates only IngredientReaction's visual proxy.</summary>
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
public sealed class ConveyorItemMotion : MonoBehaviour, IPoolable
{
    private ConveyorBeltController owner;
    private long itemId;
    private CatchableObj catchable;
    private IngredientReaction reaction;
    private Rigidbody body;
    private bool savedKinematic, savedGravity;
    private RigidbodyInterpolation savedInterpolation;
    private CollisionDetectionMode savedCollisionMode;
    private bool ownsPhysics;
    private float elapsed, phase;

    public bool CanRemainOnBelt => gameObject.activeInHierarchy
        && (catchable == null || (catchable.NetworkId == itemId && !catchable.IsHold
            && catchable.ParentEntityId <= 0 && !catchable.IsSubmissionPending && !catchable.IsSubmissionReturning))
        && (body == null || (body.isKinematic && !body.useGravity));

    public void Begin(ConveyorBeltController belt, long networkId)
    {
        End(true);
        owner = belt;
        itemId = networkId;
        catchable = GetComponent<CatchableObj>();
        reaction = GetComponent<IngredientReaction>();
        body = GetComponent<Rigidbody>();
        // Reentry may interrupt the deterministic throw simulation. Restore its native state first.
        GetComponent<IngredientTraitBounce>()?.StopSimulation();
        if (body != null)
        {
            savedKinematic = body.isKinematic;
            savedGravity = body.useGravity;
            savedInterpolation = body.interpolation;
            savedCollisionMode = body.collisionDetectionMode;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.isKinematic = true;
            body.useGravity = false;
            // The conveyor writes a pose every rendered frame; physics interpolation would overwrite it.
            body.interpolation = RigidbodyInterpolation.None;
            ownsPhysics = true;
        }
        if (catchable != null)
        {
            catchable.OnPicked += ReleaseToAnotherState;
            catchable.OnDropped += ReleaseToAnotherState;
            catchable.OnDespawned += ReleaseToAnotherState;
        }
        unchecked
        {
            uint seed = (uint)networkId ^ (uint)(networkId >> 32);
            seed = (seed ^ (seed >> 16)) * 0x7FEB352Du;
            seed = (seed ^ (seed >> 15)) * 0x846CA68Bu;
            phase = (seed & 0x00FFFFFFu) / 16777216f * Mathf.PI * 2f;
        }
        elapsed = 0f;
        RenderAtTime(0f);
    }

    private void LateUpdate()
    {
        if (owner == null) return;
        if (!CanRemainOnBelt) { ReleaseToAnotherState(); return; }
        if (StageReturnFlow.IsInputBlocked) return;
        elapsed += Time.deltaTime;
        RenderAtTime(elapsed);
    }

    public void RenderAtTime(float time)
    {
        if (owner == null || reaction == null) return;
        var settings = owner.ItemAnimation;
        if (settings == null || !settings.enabled)
        {
            reaction.ClearConveyorVisualMotion();
            return;
        }
        float blend = settings.blendInDuration > 0f
            ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(time / settings.blendInDuration)) : 1f;
        float bob = Mathf.Sin(time * settings.bobFrequency * Mathf.PI * 2f + phase) * settings.bobHeight * blend;
        float tiltPhase = time * settings.tiltFrequency * Mathf.PI * 2f + phase;
        Quaternion rotation = Quaternion.Euler(
            Mathf.Sin(tiltPhase * 0.83f) * settings.tiltAngle * blend,
            time * settings.rotationSpeed * blend,
            Mathf.Cos(tiltPhase) * settings.tiltAngle * blend);
        reaction.SetConveyorVisualMotion(Vector3.up * bob, rotation);
    }

    private void ReleaseToAnotherState()
    {
        if (owner != null) owner.UnregisterItem(itemId, false);
        else End(false);
    }

    public void End(bool restorePhysics)
    {
        if (catchable != null)
        {
            catchable.OnPicked -= ReleaseToAnotherState;
            catchable.OnDropped -= ReleaseToAnotherState;
            catchable.OnDespawned -= ReleaseToAnotherState;
        }
        reaction?.ClearConveyorVisualMotion();
        bool otherStateOwnsBody = catchable != null && (catchable.IsHold || catchable.ParentEntityId > 0
            || catchable.IsSubmissionPending || catchable.IsSubmissionReturning || catchable.NetworkId != itemId);
        // Never overwrite a held/container/throw pose or velocity after ownership has already changed.
        if (ownsPhysics && restorePhysics && !otherStateOwnsBody && body != null
            && body.isKinematic && !body.useGravity)
        {
            body.isKinematic = savedKinematic;
            body.useGravity = savedGravity;
            body.interpolation = savedInterpolation;
            body.collisionDetectionMode = savedCollisionMode;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.WakeUp();
            }
        }
        owner = null;
        ownsPhysics = false;
        elapsed = 0f;
    }

    public void ResetForPool()
    {
        if (owner != null) owner.UnregisterItem(itemId, false);
        else End(false);
    }

    private void OnDisable()
    {
        if (owner != null) owner.UnregisterItem(itemId);
        else End(false);
    }
}
