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

    [SerializeField] private Collider col;
    [SerializeField] private Rigidbody rb;

    [Header("Obj Settings")]
    [SerializeField] private CatchableObjType objType = CatchableObjType.Ingredient;
    [SerializeField] private bool canBePicked = true;
    [SerializeField] private Vector3 holdLocalPosition = Vector3.zero;
    [SerializeField] private Vector3 holdLocalEulerAngles = Vector3.zero;
    [SerializeField] private float throwForce = 0;

    public Collider Col => col;
    public Rigidbody Rb => rb;
    public bool CanBePicked => canBePicked;
    public CatchableObjType ObjType => objType;
    public Vector3 HoldLocalPosition => holdLocalPosition;
    public Vector3 HoldLocalEulerAngles => holdLocalEulerAngles;
    public float ThrowForce => throwForce;

    public bool IsHold { get; private set; } = false;

    private void OnEnable()
    {
        ResetObj();
    }
    private void Start()
    {

        if (ServerManager.Instance == null)
            return;

        // 재료는 서버 등록 안 함
        //if (objType == CatchableObjType.Ingredient)
        //    return;

        // 등록 대기열에 넣기
        ObjectNetworkRouter.Instance.EnqueueRegister(this);

        ToolRegisterPacket packet = new()
        {
            EntityId = 0, // 서버가 발급
            ToolId = (int)objType,
            Position = new System.Numerics.Vector3(transform.position.x, transform.position.y, transform.position.z),
            Quaternion = new System.Numerics.Quaternion(transform.rotation.x, transform.rotation.y, transform.rotation.z, transform.rotation.w)
        };

        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
        Debug.Log($"Tool Register Request : {name}");
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
        canBePicked = true;
        networkId = 0;
    }

    public void OnPick()
    {
        releaseFromPrep?.Invoke(this);
        releaseFromPrep = null;

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