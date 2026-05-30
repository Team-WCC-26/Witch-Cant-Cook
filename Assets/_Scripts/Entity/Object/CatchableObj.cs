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
    [SerializeField] private long networkId;
    public long NetworkId
    {
        get => networkId;
        set => networkId = value;
    }

    public CatchableData Data { get; set; }

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

    private void OnEnable()
    {
        ServerManager.Instance.RegisterHandler(_throwId, OnThrowReceived);
        ResetObj();
    }

    private void OnDisable()
    {
        ServerManager.Instance.UnRegisterHandler(_throwId);

        if (ObjectPoolManager.Instance.activeObjDict.TryGetValue(NetworkId, out UnityEngine.Object registered) && registered == this)
        {
            ObjectPoolManager.Instance.activeObjDict.Remove(NetworkId);
        }
    }

    private void OnDestroy()
    {
        if (objType == CatchableObjType.Ingredient) return;
        if (ObjectPoolManager.Instance == null) return;

        if (GameManager.Instance.catchableDics.TryGetValue(NetworkId, out CatchableObj registered) &&
            registered == this)
        {
            GameManager.Instance.catchableDics.Remove(NetworkId);
        }
    }

    private void ResetObj()
    {
        canBePicked = true;
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

    public void ChangePickState(bool isPick)
    {
        canBePicked = isPick;
    }

    private void OnThrowReceived(ReadOnlyMemory<byte> data)
    {
        IngredientThrowPacket packet =
            MemoryPackSerializer.Deserialize<IngredientThrowPacket>(data.Span);

        if (packet.EntityId != NetworkId) return;

        ApplyThrow(packet);
    }

    private void ApplyThrow(IngredientThrowPacket packet)
    {
        transform.SetParent(null, true);
        transform.position = ProtocolTypeConverter.ToUnityVector3(packet.Position);

        OnThrow();

        if (rb == null) return;

        rb.linearVelocity = ProtocolTypeConverter.ToUnityVector3(packet.Velocity);
        rb.angularVelocity = Vector3.zero;
    }
}