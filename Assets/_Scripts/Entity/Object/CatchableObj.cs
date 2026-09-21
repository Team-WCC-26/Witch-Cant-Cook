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
    [SerializeField] private LocalTransformData holdTransform = LocalTransformData.Identity;
    [SerializeField] private float throwForce = 0;


    public Collider Col => col;
    public Rigidbody Rb => rb;
    public bool CanBePicked => canBePicked;
    public EntityCategory Category => category;
    public EntityCategory AcceptedCategories =>
        Category == EntityCategory.Ingredient && acceptedCategories == EntityCategory.None
            ? GetDefaultIngredientAcceptedCategories()
            : acceptedCategories;
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

    public event Action OnPicked;
    public event Action OnDropped;

    private void Awake()
    {
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
        canBePicked = true;
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

        if (!enablePhysics)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        rb.isKinematic = !enablePhysics;
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

        if (rb == null) return;

        rb.linearVelocity = ProtocolTypeConverter.ToUnityVector3(packet.Velocity);
        rb.angularVelocity = Vector3.zero;
    }

    #region PrepInteraction
    private Action<CatchableObj> releaseFromPrep;

    public void OnPlacedOnPrep(Action<CatchableObj> releaseCallback)
    {
        releaseFromPrep = releaseCallback;

        IsHold = false;
        canBePicked = true;

        if (rb == null) return;
        if (col == null) return;

        col.enabled = true;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = true;
        rb.useGravity = false;
    }
    #endregion
}
