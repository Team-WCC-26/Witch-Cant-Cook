using Protocol;
using Server;
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

public class IngredientEgg : IngredientTrait, IPoolable
{
    [Header("References")]
    [SerializeField] private Define.eIngredient eArea = Define.eIngredient.EggInside;
    [SerializeField] private Mesh contentsMesh;
    [SerializeField] private Material[] contentsMaterials;
    [SerializeField, Min(0.1f)] private float insertResponseTimeout = 2f;

    private static readonly Dictionary<long, IngredientEgg> pendingEggs = new();
    private static readonly Dictionary<long, IngredientEgg> pendingEjections = new();
    private readonly NetworkResponseWait insertResponse = new();
    private IngredientTraitFragile fragileTrait;
    private IngredientTraitAreaCreator areaCreator;
    private IngredientDamageController damageController;
    private IngredientReaction reaction;
    private CatchableObj catchable;
    private PanInteraction pendingPan;
    private long sourceId;
    private bool wasThrown;
    private bool hasLanded;
    private bool contentsOpened;
    private bool inPan;
    private bool awaitingEjectionState;
    private bool rawAreaRequested;
    private bool originalCanBePicked;
    private bool originalDamageEnabled;

    public bool IsCooked => reaction != null && reaction.IsActionCompleted(IngredientAction.Grill);
    public bool IsRawContents => contentsOpened && !IsCooked;

    private void Awake()
    {
        fragileTrait = GetComponent<IngredientTraitFragile>();
        areaCreator = GetComponent<IngredientTraitAreaCreator>();
        damageController = GetComponent<IngredientDamageController>();
        reaction = GetComponent<IngredientReaction>();
        catchable = GetComponent<CatchableObj>();
        originalDamageEnabled = damageController != null && damageController.enabled;
        if (fragileTrait != null) fragileTrait.OnBroken += OnBroken;
        if (reaction != null) reaction.OnActionCompleted += HandleActionCompleted;
        if (catchable != null)
        {
            catchable.OnPicked += ResetFlight;
            catchable.OnDropped += ResetFlight;
            catchable.Thrown += HandleThrown;
        }
    }

    private void OnDestroy()
    {
        if (fragileTrait != null) fragileTrait.OnBroken -= OnBroken;
        if (reaction != null) reaction.OnActionCompleted -= HandleActionCompleted;
        if (catchable != null)
        {
            catchable.OnPicked -= ResetFlight;
            catchable.OnDropped -= ResetFlight;
            catchable.Thrown -= HandleThrown;
        }
    }

    private void Update() => insertResponse.Tick();

    private void OnCollisionEnter(Collision collision)
    {
        if (pendingPan == null && !IsRawContents) hasLanded = true;
    }

    public bool TryInsertIntoPan(PanInteraction pan)
    {
        if (!IsTraitAvailable || IsRawContents || pendingPan != null ||
            pan == null || !pan.CanReceiveIngredient || catchable == null || catchable.NetworkId <= 0 ||
            catchable.IsHold || !catchable.IsLocalOwner || !wasThrown || hasLanded ||
            reaction == null || contentsMesh == null || fragileTrait == null ||
            ServerManager.Instance == null || !ServerManager.Instance.IsConnected ||
            (!IsCooked && !fragileTrait.TryBreakBlock())) return false;

        pendingPan = pan;
        sourceId = catchable.NetworkId;
        originalCanBePicked = catchable.CanBePicked;
        catchable.ChangePickState(false);
        pendingEggs[sourceId] = this;
        pan.ReserveEgg(sourceId, this);
        insertResponse.Begin("계란 팬 투입", sourceId, insertResponseTimeout, CancelPanInsert);
        // Preserve the egg identity so server stats and recipe matching stay valid.
        SendInsert(new EntityInsertPacket
        {
            SubjectEntityId = sourceId,
            TargetEntityId = pan.Catchable.NetworkId
        }, sourceId).Forget();
        return true;
    }

    private async UniTask SendInsert(EntityInsertPacket packet, long requestedId)
    {
        try { await ServerManager.Instance.SendData(PacketSerializer.Serialize(packet)); }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            if (sourceId == requestedId) CancelPanInsert();
        }
    }

    public void CancelPanInsert()
    {
        insertResponse.Cancel();
        if (sourceId <= 0) return;
        pendingEggs.Remove(sourceId);
        pendingPan?.ReleaseEgg(sourceId);
        sourceId = 0;
        pendingPan = null;
        fragileTrait?.ReleaseBreakBlock();
        if (catchable != null) catchable.ChangePickState(originalCanBePicked);
    }

    // Every client changes appearance only after the server confirms pan membership.
    public void EnterPan()
    {
        bool crackSound = !contentsOpened && !IsCooked;
        CancelPanInsert();
        contentsOpened = true;
        inPan = true;
        if (catchable != null) pendingEjections.Remove(catchable.NetworkId);
        awaitingEjectionState = false;
        if (fragileTrait != null) fragileTrait.enabled = false;
        if (!IsCooked) reaction.ApplyPresentation(contentsMesh, contentsMaterials);
        if (damageController != null) damageController.enabled = IsCooked && originalDamageEnabled;
        catchable.ChangePickState(false);
        if (crackSound && Application.isPlaying)
            SoundManager.GetOrCreate().Play(SoundId.EggCrack, transform.position);
    }

    public void LeavePan() => inPan = false;

    public void HandlePanEjected()
    {
        if (!contentsOpened || IsCooked || catchable == null) return;
        inPan = false;
        awaitingEjectionState = true;
        catchable.ChangePickState(false);
        pendingEjections[catchable.NetworkId] = this;
    }

    // Eject can arrive before the same tick's CookComplete. Classify after WorldState
    // has applied all cooking flags, preventing a just-cooked egg from becoming a puddle.
    public static void ResolvePanEjections()
    {
        if (pendingEjections.Count == 0) return;
        foreach (IngredientEgg egg in new List<IngredientEgg>(pendingEjections.Values))
        {
            if (egg == null || egg.catchable == null) continue;
            long id = egg.catchable.NetworkId;
            if (egg.IsCooked || egg.catchable.ParentEntityId > 0 || !egg.catchable.IsLocalOwner)
            {
                pendingEjections.Remove(id);
                egg.awaitingEjectionState = false;
                if (egg.IsCooked && egg.catchable.ParentEntityId <= 0)
                    egg.catchable.ChangePickState(true);
                continue;
            }
            if (egg.areaCreator == null || IngredientNetworkBridge.Instance == null ||
                ServerManager.Instance == null || !ServerManager.Instance.IsConnected) continue;
            pendingEjections.Remove(id);
            egg.awaitingEjectionState = false;
            if (egg.rawAreaRequested) continue;
            egg.rawAreaRequested = true;
            // Use the existing networked slippery area, including its unpickable state.
            egg.areaCreator.CreateArea(egg.eArea);
            egg.PushIngredientToPool(egg.catchable);
        }
    }

    public static void ResetPanTransfers()
    {
        foreach (IngredientEgg egg in new List<IngredientEgg>(pendingEggs.Values))
            if (egg != null) egg.CancelPanInsert();
        pendingEggs.Clear();
        pendingEjections.Clear();
    }

    public static void HandleDestroyedEntity(long entityId)
    {
        if (pendingEggs.TryGetValue(entityId, out IngredientEgg egg)) egg.CancelPanInsert();
        pendingEjections.Remove(entityId);
    }

    public void ResetForPool()
    {
        CancelPanInsert();
        if (catchable != null) pendingEjections.Remove(catchable.NetworkId);
        wasThrown = false;
        hasLanded = false;
        contentsOpened = false;
        inPan = false;
        awaitingEjectionState = false;
        rawAreaRequested = false;
        if (damageController != null) damageController.enabled = originalDamageEnabled;
    }

    protected override void StartTrait() => ResetForPool();

    protected override void StopTrait()
    {
        CancelPanInsert();
        if (catchable != null) pendingEjections.Remove(catchable.NetworkId);
    }

    private void ResetFlight()
    {
        CancelPanInsert();
        wasThrown = false;
        hasLanded = false;
        if (IsRawContents || awaitingEjectionState) catchable.ChangePickState(false);
    }

    private void HandleThrown(Vector3 velocity)
    {
        wasThrown = velocity.sqrMagnitude > 0f;
        hasLanded = false;
        if (IsRawContents || awaitingEjectionState) catchable.ChangePickState(false);
    }

    private void HandleActionCompleted(IngredientAction action)
    {
        if ((action & IngredientAction.Grill) == 0) return;
        contentsOpened = true;
        if (fragileTrait != null) fragileTrait.enabled = false;
        if (damageController != null) damageController.enabled = originalDamageEnabled;
        if (!inPan && catchable != null && catchable.ParentEntityId <= 0)
            catchable.ChangePickState(true);
    }

    private void OnBroken(Vector3 point, Vector3 normal)
    {
        if (!IsTraitAvailable || contentsOpened) return;
        SoundManager.GetOrCreate().Play(SoundId.EggCrack, point);
        if (areaCreator != null)
        {
            areaCreator.CreateArea(eArea, point, normal);
            PushIngredientToPool(catchable);
        }
    }
}
