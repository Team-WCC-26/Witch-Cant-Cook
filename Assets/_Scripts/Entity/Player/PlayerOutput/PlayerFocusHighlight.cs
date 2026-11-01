using UnityEngine;

// Attached only to the local player. One reused effect follows one eligible catchable at a time.
[DisallowMultipleComponent]
[DefaultExecutionOrder(1000)]
public sealed class PlayerFocusHighlight : MonoBehaviour
{
    private const string ResourcePath = "VFX/InteractionFocusSparkle/InteractionFocusSparkle";
    private PlayerBrain brain;
    private PlayerInteract subscribedInteract;
    private CatchableObj focused;
    private CatchableObj suppressed;
    private long suppressedId;
    private long requestedId;
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
        CatchableObj next = brain.Interact.FindPickupFocus();
        bool stillSuppressed = next != null &&
            ((next == suppressed && next.NetworkId == suppressedId) || (requestedId != 0 && next.NetworkId == requestedId));
        if (!stillSuppressed) { suppressed = null; requestedId = 0; }
        if (next == null || stillSuppressed || !CatchableFocusQuery.TryGetBounds(next, out Bounds bounds))
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
            effect.gameObject.SetActive(true);
            effect.PlayLoop();
        }
        else effect.Follow(bounds);
    }

    private bool EnsureEffect()
    {
        if (effect != null) return true;
        if (Time.unscaledTime < retryLoadAt) return false;
        retryLoadAt = Time.unscaledTime + 1f;
        GameObject prefab = Resources.Load<GameObject>(ResourcePath);
        if (prefab == null || !prefab.TryGetComponent(out InteractionFocusSparkleVfx prefabEffect))
        {
            if (!missingAssetReported) Debug.LogWarning($"Interaction sparkle prefab missing: Resources/{ResourcePath}", this);
            missingAssetReported = true;
            return false;
        }
        effect = Instantiate(prefabEffect);
        effect.name = "Local Player Interaction Sparkle";
        effect.gameObject.SetActive(false);
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
        if (effect == null) return;
        effect.StopEffect();
        effect.gameObject.SetActive(false);
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
        if (effect != null) Destroy(effect.gameObject);
    }
}
