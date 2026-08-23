using MemoryPack;
using Protocol;
using Server;
using System;
using UnityEngine;

public enum CatchableObjType
{
    Default,
    Ingredient,
    Pan,
    Knife,
    Plate,
    Broom,
    Bucket
}

[Serializable]
public struct LocalTransformData
{
    [SerializeField] private Vector3 localPosition;
    [SerializeField] private Vector3 localEulerAngles;
    [SerializeField] private Vector3 localScale;

    public Vector3 LocalPosition => localPosition;
    public Vector3 LocalEulerAngles => localEulerAngles;
    public Vector3 LocalScale => localScale;

    public static LocalTransformData Identity => new(
        Vector3.zero,
        Vector3.zero,
        Vector3.one);

    public LocalTransformData(
        Vector3 localPosition,
        Vector3 localEulerAngles,
        Vector3 localScale)
    {
        this.localPosition = localPosition;
        this.localEulerAngles = localEulerAngles;
        this.localScale = localScale;
    }
}

public class CatchableObj : MonoBehaviour
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
    [SerializeField] private CatchableObjType objType = CatchableObjType.Ingredient;
    [SerializeField] private bool canBePicked = true;
    [SerializeField] private LocalTransformData holdTransform = LocalTransformData.Identity;
    [SerializeField] private float throwForce = 0;


    public Collider Col => col;
    public Rigidbody Rb => rb;
    public bool CanBePicked => canBePicked;
    public CatchableObjType ObjType => objType;
    public LocalTransformData HoldTransform => holdTransform;
    public float ThrowForce => throwForce;
    public bool IsEquipment { get; private set; }
    public PlayerBrain Holder { get; private set; }

    public bool IsHold { get; private set; } = false;
    public bool IsRespawning { get; set; } = false;

    private Vector3 worldScaleBeforeHold = Vector3.one;
    private bool hasHoldScaleSnapshot;
    private CatchableObj combinedVisual;

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

    private void OnEnable()
    {
        ResetObj();
    }
    private void OnDisable()
    {
        if (ObjectPoolManager.Instance.activeObjDict.TryGetValue(NetworkId, out UnityEngine.Object registered) && registered == this)
        {
            ObjectPoolManager.Instance.activeObjDict.Remove(NetworkId);
        }
    }

    private void OnDestroy()
    {
        if (objType == CatchableObjType.Ingredient) return;
        if (ObjectPoolManager.Instance == null) return;

        if (ObjectNetworkRouter.Instance.TryGet(NetworkId, out CatchableObj registered) &&
            registered == this)
        {
            ObjectNetworkRouter.Instance.Remove(NetworkId);
        }
    }

    private void ResetObj()
    {
        ReleaseCombinedVisual();
        canBePicked = true;
        networkId = 0;
        ParentEntityId = 0;
    }

    public void OnPick(PlayerBrain holder)
    {
        Holder = holder;

        worldScaleBeforeHold = transform.lossyScale;
        hasHoldScaleSnapshot = true;

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
        RestoreWorldScaleAfterHold();
        SetPhysicsState(true);
        OnDropped?.Invoke();
    }

    public void OnThrow()
    {
        Holder = null;

        IsHold = false;
        RestoreWorldScaleAfterHold();
        SetPhysicsState(true);
        OnDropped?.Invoke();
    }

    public void RestoreWorldScaleAfterHold()
    {
        if (!hasHoldScaleSnapshot) return;

        transform.localScale = worldScaleBeforeHold;
        hasHoldScaleSnapshot = false;
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
