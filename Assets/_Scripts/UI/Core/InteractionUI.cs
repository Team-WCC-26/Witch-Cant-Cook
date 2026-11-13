using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Refresh hints from the local player's held object and current interaction target.
// Formatting is read-only: never invoke gameplay actions to decide a hint.
public sealed class InteractionUI : UIBase
{
    [Header("행동 안내")]
    [SerializeField] private RectTransform primaryRow;
    [SerializeField] private RectTransform secondaryRow;
    [SerializeField] private TMP_Text primaryText;
    [SerializeField] private TMP_Text secondaryText;
    [Header("에임선 기준 배치")]
    [SerializeField] private Vector2 aimOffset = new(64f, -48f);
    [SerializeField, Min(24f)] private float rowHeight = 34f;
    [SerializeField, Min(12f)] private float textSize = 21f;
    private string displayedPrimary;
    private string displayedSecondary;

    protected override void Awake()
    {
        base.Awake();
        ApplyLayout();
        ApplyActions(string.Empty, string.Empty);
    }

    private void OnValidate() => ApplyLayout();

    private void LateUpdate()
    {
        PlayerSpawnManager players = PlayerSpawnManager.Instance;
        if (players == null || !players.TryGetPlayer(players.MyID, out PlayerBrain player) ||
            player == null || !player.isActiveAndEnabled || player.StateResolver == null ||
            player.Interact == null)
        {
            ApplyActions(string.Empty, string.Empty);
            return;
        }
        bool blocked = player.Input == null || player.Input.IsInputBlocked ||
            player.StateResolver.CurrentState.PhysicalMode != PlayerPhysicalMode.Default ||
            (player.Rocket != null && player.Rocket.IsFlying);
        SetContext(player.Interact.HeldObj,
            blocked ? null : player.Interact.FindInteractTarget(), blocked,
            player.Interact.IsPreparingThrow);
    }

    public void SetContext(CatchableObj heldObject, IInteractTarget target,
        bool interactionBlocked = false, bool preparingThrow = false)
    {
        if (interactionBlocked || (heldObject != null &&
            (heldObject.IsSubmissionPending || heldObject.IsSubmissionReturning)))
        {
            ApplyActions(string.Empty, string.Empty);
            return;
        }
        ApplyActions(GetPrimaryAction(heldObject, target), GetSecondaryAction(heldObject, preparingThrow));
    }

    public override void Opened(object[] param)
    {
        if (param == null || param.Length == 0)
        {
            ApplyActions(string.Empty, string.Empty);
            return;
        }
        SetContext(param[0] as CatchableObj,
            param.Length > 1 ? param[1] as IInteractTarget : null,
            param.Length > 2 && param[2] is bool blocked && blocked);
    }

    // Read-only queries: displaying a hint must never invoke an action or send a packet.
    public static string GetPrimaryAction(CatchableObj heldObject, IInteractTarget target)
    {
        if (target is Object unityTarget && unityTarget == null) target = null;
        if (heldObject != null && (heldObject.IsSubmissionPending || heldObject.IsSubmissionReturning))
            return string.Empty;
        if (target.GetTargetComponent<OvenButton>() != null) return "버튼 누르기";
        if (target.GetTargetComponent<Bell>() != null) return "제출";
        if (heldObject == null) return "주먹질 / 집기";
        if (heldObject.TryGetComponent<KnifeInteraction>(out _)) return "휘두르기 / 손질하기";
        if (heldObject.TryGetComponent<PanInteraction>(out PanInteraction heldPan))
            return GetPanPrimary(heldPan, target);
        if (heldObject.TryGetComponent<IngredientReaction>(out _) &&
            target.GetTargetComponent<PanInteraction>() is PanInteraction pan)
            return pan.CanReceiveHeldIngredient(heldObject) ? "넣기" : string.Empty;
        return "놓기";
    }

    public static string GetSecondaryAction(CatchableObj heldObject, bool preparingThrow = false)
    {
        if (heldObject == null || heldObject.IsSubmissionPending || heldObject.IsSubmissionReturning)
            return string.Empty;
        if (heldObject.TryGetComponent<IngredientCarrot>(out var carrot) && carrot.CanRocket)
            return "로켓 비행";
        return preparingThrow ? "놓아서 던지기" : "던지기";
    }

    public static string BuildContent(CatchableObj heldObject, IInteractTarget target)
    {
        string primary = GetPrimaryAction(heldObject, target);
        string secondary = GetSecondaryAction(heldObject);
        return (string.IsNullOrEmpty(primary) ? string.Empty : "좌클릭 - " + primary) +
            (string.IsNullOrEmpty(secondary) ? string.Empty :
                (string.IsNullOrEmpty(primary) ? string.Empty : "\n") + "우클릭 - " + secondary);
    }

    private static string GetPanPrimary(PanInteraction pan, IInteractTarget target)
    {
        if (pan.Catchable != null && pan.Catchable.NetworkId != 0 &&
            ((target.GetTargetComponent<StoveInteraction>() is StoveInteraction stove && stove.CanReceivePan) ||
             (target.GetTargetComponent<PrepInteraction>() is PrepInteraction prep && prep.CanReceivePan)))
            return "놓기";
        // Plate transfer is deliberately absent from this HUD. Do not mislabel it as an ejection.
        if (pan.CanTransferIngredient && target.GetTargetComponent<PlateInteraction>() is PlateInteraction plate &&
            plate.IsEmpty) return string.Empty;
        // Empty pans consume the click without dropping, unless a receiver accepted placement.
        return pan.HasIngredient ? "재료 꺼내기" : string.Empty;
    }

    private void ApplyActions(string primary, string secondary)
    {
        if (displayedPrimary == primary && displayedSecondary == secondary) return;
        displayedPrimary = primary;
        displayedSecondary = secondary;
        bool hasPrimary = !string.IsNullOrEmpty(primary);
        bool hasSecondary = !string.IsNullOrEmpty(secondary);
        if (primaryText != null) primaryText.text = primary;
        if (secondaryText != null) secondaryText.text = secondary;
        if (primaryRow != null) primaryRow.gameObject.SetActive(hasPrimary);
        if (secondaryRow != null)
        {
            secondaryRow.gameObject.SetActive(hasSecondary);
            secondaryRow.anchoredPosition = new Vector2(0f, hasPrimary ? -rowHeight : 0f);
        }
    }

    private void ApplyLayout()
    {
        if (transform is not RectTransform rect) return;
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = aimOffset;
        rect.sizeDelta = new Vector2(350f, rowHeight * 2f);
        if (primaryRow != null) primaryRow.sizeDelta = new Vector2(350f, rowHeight);
        if (secondaryRow != null)
        {
            secondaryRow.sizeDelta = new Vector2(350f, rowHeight);
            secondaryRow.anchoredPosition = new Vector2(0f,
                string.IsNullOrEmpty(displayedPrimary) ? 0f : -rowHeight);
        }
        if (primaryText != null) primaryText.fontSize = textSize;
        if (secondaryText != null) secondaryText.fontSize = textSize;
    }
}
