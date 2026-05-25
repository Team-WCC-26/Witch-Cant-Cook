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
public class CatchableObj : MonoBehaviour
{
    private static long nextLocalToolEntityId = -1;

    [SerializeField] private long entityId;
    public long EntityId
    {
        get => entityId;
        set => entityId = value;
    }

    private PacketId _throwId => PacketId.S_IngredientThrow;

    [SerializeField] private Collider col;
    [SerializeField] private Rigidbody rb;

    [Header("Obj Settings")]
    [SerializeField] private CatchableObjType objType = CatchableObjType.Ingredient;
    [SerializeField] private bool canBePicked = true;
    [SerializeField] private Vector3 holdLocalPosition = Vector3.zero;
    [SerializeField] private Vector3 holdLocalEulerAngles = Vector3.zero;
    [SerializeField] private float throwForce = 0;

    public Rigidbody Rb => rb;
    public bool CanBePicked => canBePicked;
    public CatchableObjType ObjType => objType;
    public Vector3 HoldLocalPosition => holdLocalPosition;
    public Vector3 HoldLocalEulerAngles => holdLocalEulerAngles;
    public float ThrowForce => throwForce;

    public bool IsHold { get; private set; } = false;

    private void Awake()
    {
        TryRegisterSceneTool();
    }

    private void OnEnable()
    {
        ServerManager.Instance.RegisterHandler(_throwId, OnThrowReceived);
    }

    private void OnDisable()
    {
        ServerManager.Instance.UnRegisterHandler(_throwId);
    }

    private void Start()
    {
        if (EntityId < 100) GameManager.Instance.catchableDics[EntityId] = this;
    }

    private void OnDestroy()
    {
        if (objType == CatchableObjType.Ingredient) return;
        if (GameManager.Instance == null) return;

        if (GameManager.Instance.catchableDics.TryGetValue(EntityId, out CatchableObj registered) &&
            registered == this)
        {
            GameManager.Instance.catchableDics.Remove(EntityId);
        }
    }

    public void OnPick()
    {
        IsHold = true;
        
        SetPhysicsState(false);
    }

    public void OnDrop()
    {
        IsHold = false;
        
        SetPhysicsState(true);
    }

    public void OnThrow()
    {
        IsHold = false;
        
        SetPhysicsState(true);
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

    private void OnThrowReceived(ReadOnlyMemory<byte> data)
    {
        IngredientThrowPacket packet =
            MemoryPackSerializer.Deserialize<IngredientThrowPacket>(data.Span);

        if (packet.EntityId != EntityId) return;

        ApplyThrow(packet);
    }

    private void ApplyThrow(IngredientThrowPacket packet)
    {
        transform.SetParent(null, true);
        transform.position = ToUnityVector3(packet.Position);

        OnThrow();

        if (rb == null) return;

        rb.linearVelocity = ToUnityVector3(packet.Velocity);
        rb.angularVelocity = Vector3.zero;
    }

    private static Vector3 ToUnityVector3(System.Numerics.Vector3 value)
    {
        return new Vector3(value.X, value.Y, value.Z);
    }

    private void TryRegisterSceneTool()
    {
        if (objType == CatchableObjType.Ingredient) return;
        if (GameManager.Instance == null) return;
        if (EntityId == 0)
        {
            EntityId = nextLocalToolEntityId--;
        }

        GameManager.Instance.catchableDics[EntityId] = this;
    }
}
