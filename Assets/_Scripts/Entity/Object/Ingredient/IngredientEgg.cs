using Protocol;
using Server;
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class IngredientEgg : IngredientTrait
{
    [Header("References")]
    [SerializeField] private Define.eIngredient eArea = Define.eIngredient.EggInside;

    private IngredientTraitFragile fragileTrait;
    private IngredientTraitAreaCreator areaCreator;

    private const int StagingDepth = 100000;
    private const long CoordinateMask = 0xFFFFFF;
    private static readonly Dictionary<long, IngredientEgg> pendingEggs = new();
    private static readonly HashSet<long> cancelledSpawns = new();
    private static IngredientNetworkBridge transferBridge;
    private CatchableObj catchable;
    private PanInteraction pendingPan;
    private IngredientNetworkBridge requestBridge;
    private long sourceId;
    private long insideId;
    private bool wasThrown;
    private bool hasLanded;
    private bool originalCanBePicked;

    #region Unity Callbacks
    private void Awake()
    {
        fragileTrait = GetComponent<IngredientTraitFragile>();
        areaCreator = GetComponent<IngredientTraitAreaCreator>();
        catchable = GetComponent<CatchableObj>();

        if (fragileTrait != null)
            fragileTrait.OnBroken += OnBroken;
        if (catchable != null)
        {
            catchable.OnPicked += ResetFlight;
            catchable.OnDropped += ResetFlight;
            catchable.Thrown += HandleThrown;
        }
    }

    private void OnDestroy()
    {
        if (fragileTrait != null)
            fragileTrait.OnBroken -= OnBroken;
        if (catchable != null)
        {
            catchable.OnPicked -= ResetFlight;
            catchable.OnDropped -= ResetFlight;
            catchable.Thrown -= HandleThrown;
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (pendingPan != null) return;
        hasLanded = true;
    }

    #endregion

    public bool TryInsertIntoPan(PanInteraction pan)
    {
        if (!IsTraitAvailable || pendingPan != null || catchable == null || catchable.NetworkId <= 0 ||
            catchable.IsHold || !catchable.IsLocalOwner || !wasThrown || hasLanded ||
            fragileTrait == null || IngredientNetworkBridge.Instance == null ||
            ServerManager.Instance == null || !fragileTrait.TryBreakBlock())
        {
            return false;
        }

        pendingPan = pan;
        requestBridge = IngredientNetworkBridge.Instance;
        sourceId = catchable.NetworkId;
        insideId = 0;
        originalCanBePicked = catchable.CanBePicked;
        catchable.ChangePickState(false);
        pendingEggs.Add(sourceId, this);
        pan.ReserveEgg(sourceId, this);

        // The unchanged server echoes Position. A unique off-map staging position
        // correlates the response without borrowing a gameplay position or changing packets.
        Vector3 position = EncodeStagingPosition(sourceId);
        IngredientSpawnPacket packet = new()
        {
            IngredientID = (int)Define.eIngredient.EggInside,
            Position = ProtocolTypeConverter.ToNumericsVector3(position),
            Quaternion = System.Numerics.Quaternion.Identity
        };
        SendRequest(PacketSerializer.Serialize(packet), this).Forget();
        return true;
    }

    public void CancelPanInsert()
    {
        if (sourceId <= 0 || !pendingEggs.Remove(sourceId)) return;
        if (requestBridge != null && requestBridge == IngredientNetworkBridge.Instance)
        {
            if (insideId > 0)
                RequestDestroy(insideId);
            else
                cancelledSpawns.Add(sourceId);
        }

        pendingPan?.ReleaseEgg(sourceId);
        pendingPan = null;
        sourceId = 0;
        insideId = 0;
        fragileTrait?.ReleaseBreakBlock();
        if (catchable != null) catchable.ChangePickState(originalCanBePicked);
    }

    public static void ResetPanTransfers()
    {
        foreach (IngredientEgg egg in new List<IngredientEgg>(pendingEggs.Values))
        {
            if (egg != null) egg.CancelPanInsert();
        }
        pendingEggs.Clear();
        // Keep same-session cancellations so their late spawn replies are still cleaned up.
        if (transferBridge != IngredientNetworkBridge.Instance)
            cancelledSpawns.Clear();
        transferBridge = IngredientNetworkBridge.Instance;
    }

    public static void PrepareSpawnedInside(int ingredientId, Vector3 position, CatchableObj inside)
    {
        if (ingredientId != (int)Define.eIngredient.EggInside ||
            !TryDecodeStagingPosition(position, out long eggId)) return;

        if (inside.TryGetComponent(out IngredientTraitArea area)) area.ConvertToIngredient();
        if (inside.ParentEntityId <= 0) inside.SetPhysicsState(false);

        if (cancelledSpawns.Remove(eggId))
        {
            RequestDestroy(inside.NetworkId);
            return;
        }
        if (!pendingEggs.TryGetValue(eggId, out IngredientEgg egg)) return;

        egg.insideId = inside.NetworkId;
        if (egg.pendingPan == null || !egg.pendingPan.isActiveAndEnabled)
        {
            egg.CancelPanInsert();
            return;
        }

        egg.pendingPan.ReserveInside(eggId, inside.NetworkId);
        EntityInsertPacket packet = new()
        {
            SubjectEntityId = inside.NetworkId,
            TargetEntityId = egg.pendingPan.Catchable.NetworkId
        };
        SendRequest(PacketSerializer.Serialize(packet), egg).Forget();
    }

    public static void CompletePanInsert(PanInteraction pan, long replacementId)
    {
        IngredientEgg source = null;
        foreach (IngredientEgg egg in pendingEggs.Values)
        {
            if (egg.pendingPan == pan && egg.insideId == replacementId)
            {
                source = egg;
                break;
            }
        }
        if (source == null) return;
        long originalId = source.sourceId;
        pendingEggs.Remove(originalId);
        source.pendingPan = null;
        source.sourceId = 0;
        source.insideId = 0;
        RequestDestroy(originalId);
    }

    public static void HandleDestroyedEntity(long entityId)
    {
        IngredientEgg cancelled = null;
        foreach (var pair in pendingEggs)
        {
            if (pair.Key == entityId || pair.Value.insideId == entityId)
            {
                cancelled = pair.Value;
                break;
            }
        }
        cancelled?.CancelPanInsert();
    }

    protected override void StartTrait()
    {
        wasThrown = false;
        hasLanded = false;
        pendingPan = null;
        sourceId = 0;
        insideId = 0;
    }

    protected override void StopTrait()
    {
        CancelPanInsert();
    }

    private void ResetFlight()
    {
        if (pendingPan != null) CancelPanInsert();
        wasThrown = false;
        hasLanded = false;
    }

    private void HandleThrown(Vector3 velocity)
    {
        wasThrown = velocity.sqrMagnitude > 0f;
        hasLanded = false;
    }

    private static Vector3 EncodeStagingPosition(long eggId)
    {
        return new Vector3(eggId & CoordinateMask, (eggId >> 24) & CoordinateMask,
            -StagingDepth - (eggId >> 48));
    }

    private static bool TryDecodeStagingPosition(Vector3 position, out long eggId)
    {
        eggId = 0;
        float high = -position.z - StagingDepth;
        if (position.x < 0f || position.x > CoordinateMask || position.y < 0f ||
            position.y > CoordinateMask || high < 0f || high > 32767f ||
            position.x != Mathf.Floor(position.x) || position.y != Mathf.Floor(position.y) ||
            high != Mathf.Floor(high)) return false;
        eggId = (long)position.x | ((long)position.y << 24) | ((long)high << 48);
        return eggId > 0;
    }

    private static async UniTask SendRequest(byte[] packet, IngredientEgg source)
    {
        long requestSourceId = source.sourceId;
        try
        {
            await ServerManager.Instance.SendData(packet);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, source);
            if (source != null && source.sourceId == requestSourceId)
                source.CancelPanInsert();
        }
    }

    private static void RequestDestroy(long entityId)
    {
        if (ServerManager.Instance == null) return;
        EntityDestroyPacket packet = new() { EntityId = entityId };
        ServerManager.Instance.SendData(PacketSerializer.Serialize(packet)).Forget();
    }

    private void OnBroken(Vector3 point, Vector3 normal)
    {
        if (!IsTraitAvailable) return;
        SoundManager.GetOrCreate().Play(SoundId.EggCrack, point);
        if (areaCreator != null)
        {
            areaCreator.CreateArea(eArea, point, normal);
            PushIngredientToPool(catchable);
        }
    }
}
