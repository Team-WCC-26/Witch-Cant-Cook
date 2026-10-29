using Protocol;
using System;
using UnityEngine;

[Serializable]
public struct LocalTransformData
{
    [SerializeField] private Vector3 localPosition;
    [SerializeField] private Vector3 localEulerAngles;

    public Vector3 LocalPosition => localPosition;
    public Vector3 LocalEulerAngles => localEulerAngles;

    public static LocalTransformData Identity => new(
        Vector3.zero,
        Vector3.zero);

    public LocalTransformData(
        Vector3 localPosition,
        Vector3 localEulerAngles)
    {
        this.localPosition = localPosition;
        this.localEulerAngles = localEulerAngles;
    }
}

public enum HeldAttachmentPoint
{
    [InspectorName("자동 (기존 방식)")] Automatic = 0,
    [InspectorName("공통 위치 (ItemPoint)")] ItemPoint = 1,
    [InspectorName("오른손")] RightHand = 2,
    [InspectorName("왼손")] LeftHand = 3
}

public class CatchableObj : MonoBehaviour, IPoolable, IInteractTarget
{
    [SerializeField] private long networkId;
    public long NetworkId
    {
        get => networkId;
        set => networkId = value;
    }

    public long ParentEntityId { get; set; }

    public CatchableData Data { get; set; }

    [SerializeField] private Collider col;
    [SerializeField] private Rigidbody rb;

    [Header("Obj Settings")]
    [Tooltip("obj type")]
    [SerializeField] private EntityCategory category = EntityCategory.None;
    [Tooltip("Interactable 여부")]
    [SerializeField] private EntityCategory acceptedCategories = EntityCategory.None;
    [SerializeField] private bool canBePicked = true;
    [Tooltip("잡았을 때 따라갈 위치. Hold Transform은 선택한 위치를 기준으로 적용됩니다.")]
    [SerializeField] private HeldAttachmentPoint holdAttachment = HeldAttachmentPoint.Automatic;
    [SerializeField] private LocalTransformData holdTransform = LocalTransformData.Identity;
    [SerializeField] private float throwForce = 0;


    public Collider Col => col;
    public Rigidbody Rb => rb;
    public Vector3 LinearVelocity => TryGetComponent(out IngredientTraitBounce bounce) && bounce.IsSimulating
        ? bounce.Velocity : rb != null ? rb.linearVelocity : Vector3.zero;
    public bool CanBePicked => canBePicked && !IsSubmissionPending && !IsSubmissionReturning;
    public bool IsSubmissionPending { get; private set; }
    public bool IsSubmissionReturning { get; private set; }
    public void SetSubmissionReturning(bool returning) => IsSubmissionReturning = returning;

    // Shared by submission zones on this client; reset when the object is recycled.
    public bool TryBeginSubmission()
    {
        if (IsSubmissionPending || IsSubmissionReturning || IsHold || Holder != null) return false;
        IsSubmissionPending = true;
        return true;
    }

    // A server-confirmed food removal can leave a live empty plate behind.
    public void ReleaseSubmissionLock() => IsSubmissionPending = false;
    public EntityCategory Category => category;
    public EntityCategory AcceptedCategories =>
        Category == EntityCategory.Ingredient && acceptedCategories == EntityCategory.None
            ? GetDefaultIngredientAcceptedCategories()
            : acceptedCategories;
    public HeldAttachmentPoint HoldAttachment => holdAttachment;

    public Transform ResolveHoldParent(PlayerBrain holder)
    {
        if (holder == null) return null;
        if (holdAttachment == HeldAttachmentPoint.Automatic)
            return IsEquipment ? holder.EquipPoint : holder.ItemPoint;
        if (holdAttachment == HeldAttachmentPoint.ItemPoint)
            return holder.ItemPoint;

        Animator animator = holder.Animator;
        if (animator != null && animator.isHuman)
        {
            HumanBodyBones bone = holdAttachment == HeldAttachmentPoint.LeftHand
                ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
            Transform hand = animator.GetBoneTransform(bone);
            if (hand != null) return hand;
        }

        return holdAttachment == HeldAttachmentPoint.RightHand && holder.EquipPoint != null
            ? holder.EquipPoint : holder.ItemPoint;
    }

    public LocalTransformData HoldTransform => holdTransform;
    public float ThrowForce => throwForce;
    public bool IsEquipment { get; private set; }
    public PlayerBrain Holder { get; private set; }

    private static EntityCategory GetDefaultIngredientAcceptedCategories()
    {
        return EntityCategory.Player |
               EntityCategory.Knife |
               EntityCategory.Plate;
    }

    // Keep collision authority after OnDrop/OnThrow clears Holder.
    public string LastHolderPlayerId { get; private set; }
    public bool IsLocalOwner =>
        !string.IsNullOrEmpty(LastHolderPlayerId) &&
        PlayerSpawnManager.Instance != null &&
        PlayerSpawnManager.Instance.IsMine(LastHolderPlayerId);

    public bool IsHold { get; private set; } = false;
    public bool IsRespawning { get; set; } = false;

    private CatchableObj combinedVisual;
    private ObjectNetworkRouter objectRouter;
    private bool initialCanBePicked;
    private RigidbodyInterpolation initialInterpolation;

    public event Action OnPicked;
    public event Action OnDropped;
    public event Action<Vector3> Thrown;

    private void Awake()
    {
        initialCanBePicked = canBePicked;
        if (rb != null)
        {
            initialInterpolation = rb.interpolation;
            rb.collisionDetectionMode = rb.isKinematic
                ? CollisionDetectionMode.ContinuousSpeculative : CollisionDetectionMode.ContinuousDynamic;
        }

        if (col == null || col.isTrigger)
        {
            foreach (Collider candidate in GetComponentsInChildren<Collider>())
            {
                if (candidate.isTrigger) continue;

                col = candidate;
                break;
            }
        }

        if (col == null)
            throw new InvalidOperationException($"{name} requires a non-trigger Collider.");

        foreach (MonoBehaviour behaviour in GetComponents<MonoBehaviour>())
        {
            if (behaviour is not IEquipment) continue;

            IsEquipment = true;
            break;
        }
    }

    private void OnDestroy()
    {
        if (Category == EntityCategory.Ingredient) return;
        if (ObjectPoolManager.Instance == null) return;

        if (objectRouter != null &&
            objectRouter.TryGet(NetworkId, out CatchableObj registered) &&
            registered == this)
        {
            objectRouter.Remove(NetworkId);
        }
    }

    public void SetNetworkRouter(ObjectNetworkRouter router)
    {
        objectRouter = router;
    }

    public void ResetForPool()
    {
        if (ObjectPoolManager.Instance != null &&
            ObjectPoolManager.Instance.activeObjDict.TryGetValue(NetworkId, out UnityEngine.Object registered) &&
            registered == gameObject)
        {
            ObjectPoolManager.Instance.activeObjDict.Remove(NetworkId);
        }

        if (objectRouter != null &&
            objectRouter.TryGet(NetworkId, out CatchableObj routed) &&
            routed == this)
        {
            objectRouter.Remove(NetworkId);
        }

        ReleaseCombinedVisual();

        if (Holder != null)
        {
            Holder.Interact.TryReleaseHeld(this);
        }

        Holder = null;
        LastHolderPlayerId = null;
        IsHold = false;
        IsRespawning = false;
        IsSubmissionPending = false;
        IsSubmissionReturning = false;
        canBePicked = initialCanBePicked;
        releaseFromPrep = null;

        networkId = 0;
        ParentEntityId = 0;
    }

    public void OnPick(PlayerBrain holder)
    {
        Holder = holder;
        LastHolderPlayerId = holder != null ? holder.PlayerId : null;

        releaseFromPrep?.Invoke(this);
        releaseFromPrep = null;

        IsHold = true;
        SetPhysicsState(false);
        OnPicked?.Invoke();
    }

    public void OnDrop()
    {
        Holder = null;

        IsHold = false;
        canBePicked = true;
        SetPhysicsState(true);
        OnDropped?.Invoke();
    }

    public void OnThrow()
    {
        Holder = null;

        IsHold = false;
        canBePicked = true;
        SetPhysicsState(true);
        OnDropped?.Invoke();
    }

    public void SetPhysicsState(bool enablePhysics)
    {
        if (rb == null) return;
        col.enabled = enablePhysics;

        // Held objects follow the hand transform directly; interpolation would lag that pose.
        rb.interpolation = enablePhysics ? initialInterpolation : RigidbodyInterpolation.None;

        if (!enablePhysics && !rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        GetComponent<IngredientTraitBounce>()?.StopSimulation(false);
        if (!enablePhysics) rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        rb.isKinematic = !enablePhysics;
        if (enablePhysics) rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.useGravity = enablePhysics;
    }

    public void ChangePickState(bool isPick)
    {
        canBePicked = isPick;
    }

    public void AttachCombinedVisual(CatchableObj visual)
    {
        // Owned visual
        combinedVisual = visual;
    }

    public void ReleaseCombinedVisual()
    {
        // Child cleanup
        if (combinedVisual == null) return;

        CatchableObj visual = combinedVisual;
        combinedVisual = null;
        visual.transform.SetParent(null, true);
        ObjectPoolManager.Instance.Push(visual.gameObject);
    }

    public void ApplyThrow(EntityThrowPacket packet)
    {
        transform.SetParent(null, true);
        transform.position = ProtocolTypeConverter.ToUnityVector3(packet.Position);

        OnThrow();
        Thrown?.Invoke(ProtocolTypeConverter.ToUnityVector3(packet.Velocity));

        if (rb == null) return;

        rb.linearVelocity = ProtocolTypeConverter.ToUnityVector3(packet.Velocity);
        rb.angularVelocity = Vector3.zero;
        GetComponent<IngredientTraitBounce>()?.BeginMotion(
            ProtocolTypeConverter.ToUnityVector3(packet.Position), ProtocolTypeConverter.ToUnityVector3(packet.Velocity));
    }

    #region PrepInteraction
    private Action<CatchableObj> releaseFromPrep;

    public void OnPlacedOnPrep(Action<CatchableObj> releaseCallback)
    {
        GetComponent<IngredientTraitBounce>()?.StopSimulation(false);
        releaseFromPrep = releaseCallback;

        IsHold = false;
        canBePicked = true;

        if (rb == null) return;
        if (col == null) return;

        col.enabled = true;
        if (!rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        rb.isKinematic = true;
        rb.useGravity = false;
    }
    #endregion
}
