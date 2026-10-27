using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Refresh hints from the local player's held object and current interaction target.
// Formatting is read-only: never invoke gameplay actions to decide a hint.
public sealed class InteractionUI : UIBase
{
    [SerializeField] private TMP_Text contentText;
    [SerializeField] private Image background;
    private string displayedContent;

    #region Unity Callbacks
    private void LateUpdate()
    {
        PlayerSpawnManager players = PlayerSpawnManager.Instance;
        if (players == null || !players.TryGetPlayer(players.MyID, out PlayerBrain player) ||
            player == null || !player.isActiveAndEnabled || player.StateResolver == null)
        {
            ApplyContent(string.Empty);
            return;
        }

        bool blocked = player.Input == null || player.Input.IsInputBlocked ||
            player.StateResolver.CurrentState.PhysicalMode != PlayerPhysicalMode.Default ||
            (player.Rocket != null && player.Rocket.IsFlying);
        SetContext(player.Interact.HeldObj, blocked ? null : player.Interact.FindInteractTarget(), blocked);
    }
    #endregion

    public void SetContext(CatchableObj heldObject, IInteractTarget target, bool interactionBlocked = false)
    {
        ApplyContent(interactionBlocked ? string.Empty : BuildContent(heldObject, target));
    }

    public override void Opened(object[] param)
    {
        if (param == null || param.Length == 0)
        {
            ApplyContent(string.Empty);
            return;
        }
        SetContext(param[0] as CatchableObj,
            param.Length > 1 ? param[1] as IInteractTarget : null,
            param.Length > 2 && param[2] is bool blocked && blocked);
    }

    public static string BuildContent(CatchableObj heldObject, IInteractTarget target)
    {
        if (target is Object unityTarget && unityTarget == null) target = null;
        if (heldObject == null)
        {
            if (target is CatchableObj item)
                return item.CanBePicked && !item.IsHold && item.gameObject.activeInHierarchy
                    ? "좌클릭 - 잡기" : string.Empty;
            return "좌클릭 - " + GetEmptyHandPrimary(target);
        }

        if (heldObject.IsSubmissionPending || heldObject.IsSubmissionReturning) return string.Empty;
        string primary;
        // Match PlayerInteract's priority: oven button, held action, receiver, drop.
        if (target.GetTargetComponent<OvenButton>() != null) primary = "누르기";
        else if (heldObject.TryGetComponent<KnifeInteraction>(out _))
            primary = target.GetTargetComponent<Bell>() != null ? "제출" : "공격";
        else if (heldObject.TryGetComponent<PanInteraction>(out PanInteraction heldPan))
            primary = GetPanPrimary(heldPan, target);
        else if (target.GetTargetComponent<PanInteraction>() is PanInteraction pan && pan.CanReceiveIngredient &&
            heldObject.TryGetComponent<IngredientReaction>(out _) &&
            heldObject.Data?.id != (int)Define.eIngredient.EggInside && heldObject.NetworkId != 0)
            primary = "넣기";
        else primary = "놓기";

        return string.IsNullOrEmpty(primary) ? string.Empty : $"좌클릭 - {primary}";
    }

    private static string GetEmptyHandPrimary(IInteractTarget target)
    {
        if (target.GetTargetComponent<OvenButton>() != null) return "누르기";
        if (target.GetTargetComponent<Bell>() != null) return "제출";
        if (target.GetTargetComponent<StagePageCylinder>() is StagePageCylinder page)
            return page.Direction == StagePageCylinder.PageDirection.Previous ? "이전" : "다음";
        return "공격";
    }

    private static string GetPanPrimary(PanInteraction pan, IInteractTarget target)
    {
        if (pan.Catchable != null && pan.Catchable.NetworkId != 0 &&
            ((target.GetTargetComponent<StoveInteraction>() is StoveInteraction stove && stove.CanReceivePan) ||
             (target.GetTargetComponent<PrepInteraction>() is PrepInteraction prep && prep.CanReceivePan)))
            return "놓기";
        if (pan.HasIngredient && target.GetTargetComponent<PlateInteraction>() is PlateInteraction plate && plate.IsEmpty)
            return "담기";
        return pan.HasIngredient ? "배출" : string.Empty;
    }

    private void ApplyContent(string content)
    {
        if (displayedContent == content) return;
        displayedContent = content;
        bool visible = !string.IsNullOrWhiteSpace(content);
        if (contentText != null)
        {
            contentText.text = visible ? content : string.Empty;
            contentText.gameObject.SetActive(visible);
        }
        if (background != null) background.enabled = visible;
    }

}
