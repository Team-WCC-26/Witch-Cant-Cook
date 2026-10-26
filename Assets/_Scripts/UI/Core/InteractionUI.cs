using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The player supplies its held object and current interaction target.
// Formatting is read-only: never invoke gameplay actions to decide a hint.
public sealed class InteractionUI : UIBase
{
    [SerializeField] private TMP_Text contentText;
    [SerializeField] private Image background;

    public void SetContext(CatchableObj heldObject, IInteractTarget target, bool interactionBlocked = false)
    {
        ApplyContent(interactionBlocked ? string.Empty : BuildContent(heldObject, target));
    }

    public static string BuildContent(CatchableObj heldObject, IInteractTarget target)
    {
        if (target is Object unityTarget && unityTarget == null) target = null;
        if (heldObject == null)
        {
            if (target is CatchableObj item)
                return item.CanBePicked && !item.IsHold && item.gameObject.activeInHierarchy
                    ? $"좌클릭 - {DisplayName(item)} 잡기" : string.Empty;
            return target.GetTargetComponent<IPunchReceiver>() != null
                ? "좌클릭 - " + (target.GetTargetComponent<OvenButton>() != null ? "오븐 버튼 누르기" : "상호작용하기")
                : string.Empty;
        }

        if (heldObject.IsSubmissionPending || heldObject.IsSubmissionReturning) return string.Empty;
        string name = DisplayName(heldObject);
        string primary;
        // Match PlayerInteract's priority: oven button, held action, receiver, drop.
        if (target.GetTargetComponent<OvenButton>() != null) primary = "오븐 버튼 누르기";
        else if (heldObject.TryGetComponent<KnifeInteraction>(out _)) primary = "칼 휘두르기";
        else if (heldObject.TryGetComponent<PanInteraction>(out _)) primary = "팬 사용하기";
        else if (target.GetTargetComponent<PanInteraction>() is PanInteraction pan && !pan.HasIngredient &&
            heldObject.TryGetComponent<IngredientReaction>(out _) &&
            heldObject.Data?.id != (int)Define.eIngredient.EggInside)
            primary = name + " 팬에 넣기";
        else primary = name + " 내려놓기";

        string secondary = heldObject.TryGetComponent<IngredientCarrot>(out var carrot) && carrot.CanRocket
            ? "당근 로켓 타기" : name + " 던지기";
        return $"좌클릭 - {primary}\n우클릭 - {secondary}";
    }

    private static string DisplayName(CatchableObj item)
    {
        if (!string.IsNullOrWhiteSpace(item.Data?.name)) return item.Data.name;
        return item.Category switch
        {
            EntityCategory.Knife => "칼",
            EntityCategory.Pan => "팬",
            EntityCategory.Plate => "접시",
            EntityCategory.Ingredient => "재료",
            _ => "물체"
        };
    }

    private void ApplyContent(string content)
    {
        bool visible = !string.IsNullOrWhiteSpace(content);
        if (contentText != null)
        {
            contentText.text = visible ? content : string.Empty;
            contentText.gameObject.SetActive(visible);
        }
        if (background != null) background.enabled = visible;
    }

    public override void Opened(object[] param)
    {
        SetContext(param != null && param.Length > 0 ? param[0] as CatchableObj : null,
            param != null && param.Length > 1 ? param[1] as IInteractTarget : null,
            param != null && param.Length > 2 && param[2] is bool blocked && blocked);
    }
}
