using UnityEngine;

// Attached only to the local player. One reused effect follows one eligible catchable at a time.
[DisallowMultipleComponent]
[DefaultExecutionOrder(1000)]
public sealed class PlayerFocusHighlight : MonoBehaviour
{
    [SerializeField, Min(.1f), InspectorName("원거리 표시 거리")] private float previewDistance = 30f;
    [SerializeField, Range(.01f, .5f), InspectorName("원거리 에임 허용 범위")] private float previewViewportRadius = .12f;
    public bool IsPickupReady { get; private set; }
    private const string ResourcePath = "VFX/InteractionFocusSparkle/InteractionFocusSparkle";
    private PlayerBrain brain;
    private PlayerInteract subscribedInteract;
    private CatchableObj focused;
    private CatchableObj suppressed;
    private long suppressedId;
    private long requestedId;
    private readonly System.Collections.Generic.List<InteractionFocusSparkleVfx> effectPool = new();
    private InteractionFocusSparkleVfx effectPrefab;
    private InteractionFocusSparkleVfx effect;
    private float retryLoadAt;
    private bool missingAssetReported;

    public CatchableObj FocusedItem => focused;

    public void Initialize(PlayerBrain player)
    {
        brain = player;
        Subscribe();
    }

    private void OnEnable() => Subscribe();

    private void Subscribe()
    {
        PlayerInteract interact = brain != null ? brain.Interact : null;
        if (subscribedInteract == interact) return;
        if (subscribedInteract != null) subscribedInteract.EntityInteractionRequested -= OnInteractionRequested;
        subscribedInteract = interact;
        if (subscribedInteract != null) subscribedInteract.EntityInteractionRequested += OnInteractionRequested;
    }

    private void LateUpdate()
    {
        if (brain == null || brain.StateResolver is not LocalPlayerStateResolver || brain.Input == null ||
            !brain.Input.isActiveAndEnabled || brain.Input.IsInputBlocked || brain.PlayerCam == null ||
            !brain.PlayerCam.isActiveAndEnabled || brain.StateResolver.CurrentState.PhysicalMode != PlayerPhysicalMode.Default ||
            (brain.Rocket != null && brain.Rocket.IsFlying))
        {
            HideFocus();
            return;
        }

        Subscribe();
        CatchableObj next = brain.Interact.FindFocus(previewDistance, previewViewportRadius, out bool pickupReady);
        bool stillSuppressed = next != null &&
            ((next == suppressed && next.NetworkId == suppressedId) || (requestedId != 0 && next.NetworkId == requestedId));
        if (!stillSuppressed) { suppressed = null; requestedId = 0; }
        if (next == null || stillSuppressed || !TryGetFocusBounds(next, out Bounds bounds))
        {
            HideFocus();
            return;
        }
        if (focused != next || effect == null)
        {
            HideFocus();
            if (!EnsureEffect()) return;
            focused = next;
            focused.OnPicked += OnFocusedPicked;
            focused.OnDespawned += OnFocusedDespawned;
            effect.Follow(bounds);
            effect.SetPickupReady(pickupReady);
            effect.FadeIn();
        }
        else
        {
            effect.Follow(bounds);
            effect.SetPickupReady(pickupReady);
        }
        IsPickupReady = pickupReady;
    }

    private bool EnsureEffect()
    {
        if (effect != null) return true;
        foreach (InteractionFocusSparkleVfx pooled in effectPool)
        {
            if (pooled == null || pooled.gameObject.activeSelf) continue;
            effect = pooled;
            return true;
        }
        if (effectPrefab == null)
        {
            if (Time.unscaledTime < retryLoadAt) return false;
            GameObject prefab = Resources.Load<GameObject>(ResourcePath);
            if (prefab == null || !prefab.TryGetComponent(out effectPrefab))
            {
                retryLoadAt = Time.unscaledTime + 1f;
                if (!missingAssetReported) Debug.LogWarning($"Interaction sparkle prefab missing: Resources/{ResourcePath}", this);
                missingAssetReported = true;
                return false;
            }
        }
        effect = Instantiate(effectPrefab);
        effect.name = "Local Player Interaction Sparkle";
        effect.gameObject.SetActive(false);
        effectPool.Add(effect);
        return true;
    }

    private void OnInteractionRequested(long targetId)
    {
        requestedId = targetId;
        if (focused != null)
        {
            suppressed = focused;
            suppressedId = focused.NetworkId;
        }
        HideFocus();
    }

    private void OnFocusedPicked() => OnInteractionRequested(focused != null ? focused.NetworkId : 0);

    private void OnFocusedDespawned()
    {
        suppressed = null;
        requestedId = 0;
        HideFocus();
    }

    private void HideFocus()
    {
        if (focused != null)
        {
            focused.OnPicked -= OnFocusedPicked;
            focused.OnDespawned -= OnFocusedDespawned;
        }
        focused = null;
        IsPickupReady = false;
        if (effect == null) return;
        effect.FadeOut();
        effect = null;
    }

    private void OnDisable()
    {
        HideFocus();
        suppressed = null;
        requestedId = 0;
        if (subscribedInteract != null) subscribedInteract.EntityInteractionRequested -= OnInteractionRequested;
        subscribedInteract = null;
    }

    private void OnDestroy()
    {
        foreach (InteractionFocusSparkleVfx pooled in effectPool)
            if (pooled != null) Destroy(pooled.gameObject);
        effectPool.Clear();
    }


    private static bool TryGetFocusBounds(CatchableObj item, out Bounds bounds)
    {
        Collider collider = item != null ? item.Col : null;
        bounds = collider != null ? collider.bounds : default;
        return collider != null && collider.enabled && !collider.isTrigger && collider.gameObject.activeInHierarchy;
    }
}
