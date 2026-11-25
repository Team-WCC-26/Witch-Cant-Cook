using System.Globalization;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

// Keep this controller active while switching its two child/sibling canvases.
// The stamp uses the world canvas GraphicRaycaster and the local settlement camera.
public sealed class StageSettlementWorldUI : MonoBehaviour
{
    [SerializeField] private GameObject recipeCanvas;
    [SerializeField] private GameObject settlementCanvas;
    [SerializeField] private TMP_Text stageText;
    [SerializeField] private TMP_Text completedCountText;
    [SerializeField] private TMP_Text earnedMoneyText;
    [SerializeField] private TMP_Text goalText;
    [SerializeField] private TMP_Text outcomeText;
    [SerializeField] private TMP_Text[] confirmationTexts = new TMP_Text[2];
    [SerializeField] private TMP_Text returnHintText;
    [SerializeField] private TMP_Text issueText;
    [Header("Confirmation Stamp")]
    [SerializeField] private Button confirmButton;
    [SerializeField] private Image confirmStampImage;
    [SerializeField] private TMP_Text confirmButtonText;
    [Tooltip("임시 원형 대신 사용할 성공 도장 이미지입니다.")]
    [SerializeField] private Sprite successStamp;
    [Tooltip("임시 원형 대신 사용할 실패 도장 이미지입니다.")]
    [SerializeField] private Sprite failureStamp;
    private Canvas interactionCanvas;

    private RoomHudSession room;
    private StageReturnFlow flow;
    private bool originalRecipeActive;
    private int loggedRun;
    private static bool ensuringBoards;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState() => ensuringBoards = false;

    public static async UniTask EnsureLoadedBoards()
    {
        if (ensuringBoards) return;
        ensuringBoards = true;
        try
        {
            var boards = new List<StageSettlementWorldUI>(FindObjectsByType<StageSettlementWorldUI>(
                FindObjectsInactive.Include, FindObjectsSortMode.None));
            GameObject prefab = null;
            foreach (RecipeScrollView recipe in FindObjectsByType<RecipeScrollView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (recipe == null || recipe.transform.parent == null || !recipe.transform.parent.gameObject.activeInHierarchy) continue;
                if (!IsRecipeBig(recipe.transform)) continue;
                bool created = false;
                StageSettlementWorldUI board = boards.Find(candidate => candidate != null &&
                    (candidate.recipeCanvas == recipe.gameObject ||
                    (candidate.recipeCanvas == null && candidate.transform.parent == recipe.transform.parent)));
                if (board == null)
                {
                    ResourceManager resources = ResourceManager.Instance;
                    if (resources == null) throw new System.InvalidOperationException("Settlement board requires ResourceManager.");
                    if (prefab == null) prefab = await resources.LoadAsync<GameObject>("StageSettlementWorldCanvas");
                    if (recipe == null) continue;
                    if (prefab == null) throw new System.InvalidOperationException("Addressable StageSettlementWorldCanvas is missing.");
                    // Recheck after loading; another scene instance may have become available.
                    board = System.Array.Find(FindObjectsByType<StageSettlementWorldUI>(FindObjectsInactive.Include, FindObjectsSortMode.None),
                        candidate => candidate.recipeCanvas == recipe.gameObject);
                    if (board == null)
                    {
                        GameObject root = Instantiate(prefab, recipe.transform.parent, false);
                        board = root.GetComponent<StageSettlementWorldUI>();
                        if (board == null) { Destroy(root); throw new System.InvalidOperationException("Settlement prefab has no StageSettlementWorldUI."); }
                        boards.Add(board);
                        created = true;
                    }
                }
                board.BindRecipe(recipe, created);
            }
        }
        catch (System.Exception error) { Debug.LogException(error); }
        finally { ensuringBoards = false; }
    }

    private static bool IsRecipeBig(Transform node)
    {
        for (Transform current = node; current != null; current = current.parent)
            if (current.name == "Recipe_Big") return true;
        return false;
    }

    private void BindRecipe(RecipeScrollView recipe, bool alignToRecipe)
    {
        if (recipeCanvas != recipe.gameObject)
        {
            recipeCanvas = recipe.gameObject;
            originalRecipeActive = recipeCanvas.activeSelf;
        }
        // Preserve the authored transform of a scene prefab instance. Only newly loaded
        // instances need an initial pose based on the recipe canvas.
        if (alignToRecipe)
        {
            transform.SetParent(recipe.transform.parent, false);
            if (transform is RectTransform destination && recipe.transform is RectTransform source)
            {
                destination.anchorMin = source.anchorMin;
                destination.anchorMax = source.anchorMax;
                destination.pivot = source.pivot;
                destination.sizeDelta = source.sizeDelta;
            }
            transform.localScale = recipe.transform.localScale;
            transform.SetPositionAndRotation(recipe.transform.position, recipe.transform.rotation);
        }
        // Scene instances can start inactive, so OnEnable has not initialized their sessions yet.
        // Keep the controller active; Refresh only toggles the child canvases.
        if (!enabled) enabled = true;
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        if (isActiveAndEnabled) Refresh();
    }

    private void OnEnable()
    {
        originalRecipeActive = recipeCanvas != null && recipeCanvas.activeSelf;
        room = RoomHudSession.Instance;
        flow = StageReturnFlow.Instance;
        EnsureConfirmationControls();
        room.Changed += Refresh;
        flow.Changed += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        if (confirmButton != null) confirmButton.onClick.RemoveListener(OnConfirmClicked);
        if (room != null) room.Changed -= Refresh;
        if (flow != null) flow.Changed -= Refresh;
        if (recipeCanvas != null) recipeCanvas.SetActive(originalRecipeActive);
        if (settlementCanvas != null) settlementCanvas.SetActive(false);
    }

    private void Refresh()
    {
        bool settling = flow.State.ShowsSettlement && flow.State.Settlement != null;
        bool visible = settling && IsRecipeBig(recipeCanvas != null ? recipeCanvas.transform : transform);
        if (recipeCanvas != null) recipeCanvas.SetActive(!settling && originalRecipeActive);
        if (settlementCanvas != null) settlementCanvas.SetActive(visible);
        if (!visible) return;

        // Reuse the authored camera, or create one for clients whose scene snapshot lacks it.
        RectTransform cameraCanvas = settlementCanvas != null ? settlementCanvas.transform as RectTransform : transform as RectTransform;
        StageSettlementCamera cameraController = StageSettlementCamera.EnsureForScroll(cameraCanvas);
        if (interactionCanvas != null && cameraController != null)
            interactionCanvas.worldCamera = cameraController.RenderingCamera;
        StageSettlementData result = flow.State.Settlement;
        if (recipeCanvas != null && loggedRun != result.RunNumber)
        {
            loggedRun = result.RunNumber;
            Debug.Log($"[Settlement Board] player={room.Lobby.LocalPlayerId}, recipe={recipeCanvas.transform.parent?.name}, visible={settlementCanvas != null && settlementCanvas.activeInHierarchy}", this);
        }
        if (stageText != null) stageText.text = $"STAGE {result.StageNumber}";
        if (completedCountText != null) completedCountText.text = result.CompletedDishCount.ToString("00", CultureInfo.InvariantCulture);
        if (earnedMoneyText != null) earnedMoneyText.text = Format(result.EarnedMoney);
        if (goalText != null) goalText.text = result.Goal.HasValue
            ? $"{Format(result.Goal.Value)}콩 벌기 ({Format(result.EarnedMoney)}/{Format(result.Goal.Value)})"
            : $"목표 금액 미확인 · 획득 {Format(result.EarnedMoney)}콩";
        if (outcomeText != null)
        {
            outcomeText.text = result.IsSuccess ? "목표 달성" : "목표 미달성";
            outcomeText.color = result.IsSuccess ? new Color(.5f, .87f, .66f) : new Color(1f, .58f, .49f);
        }

        for (int i = 0; i < confirmationTexts.Length; i++)
        {
            TMP_Text text = confirmationTexts[i];
            if (text == null) continue;
            if (i >= room.Lobby.Players.Count) { text.text = $"{i + 1}P\n참가자 없음"; continue; }
            string id = room.Lobby.Players[i];
            bool local = id == room.Lobby.LocalPlayerId;
            string state = flow.HasResultConfirmation(id) ? "확인 전송됨" :
                local ? (flow.IsConfirming ? "확인 전송 중" : "확인 전") : "확인 정보 대기";
            text.text = $"{i + 1}P{(local ? " (나)" : "")}\n{state}";
        }
        if (returnHintText != null) returnHintText.text = flow.State.Phase switch
        {
            StageReturnPhase.Returning => "로비로 이동 중",
            StageReturnPhase.WaitingForServer => "다른 플레이어 응답 대기 중..",
            _ => "도장을 클릭하여 확인\n두 플레이어 모두 확인하면 로비로 복귀합니다."
        };
        RefreshConfirmationStamp(result);
        if (issueText != null) issueText.text = flow.ConfirmationIssue ?? string.Empty;
    }

    private void EnsureConfirmationControls()
    {
        if (settlementCanvas == null) return;
        interactionCanvas = settlementCanvas.GetComponent<Canvas>();
        if (interactionCanvas != null)
        {
            GraphicRaycaster raycaster = interactionCanvas.GetComponent<GraphicRaycaster>();
            if (raycaster == null) raycaster = interactionCanvas.gameObject.AddComponent<GraphicRaycaster>();
            raycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;
        }
        CanvasGroup group = settlementCanvas.GetComponent<CanvasGroup>();
        if (group != null) { group.interactable = true; group.blocksRaycasts = true; }

        // Older multiplayer scene snapshots may not contain the new prefab child yet.
        if (confirmButton == null)
        {
            Transform existing = settlementCanvas.transform.Find("Confirmation Stamp");
            if (existing != null) confirmButton = existing.GetComponent<Button>();
            if (confirmButton == null)
            {
                var stamp = new GameObject("Confirmation Stamp", typeof(RectTransform), typeof(CanvasRenderer), typeof(SettlementStampImage), typeof(Button));
                stamp.layer = settlementCanvas.layer;
                RectTransform rect = stamp.GetComponent<RectTransform>();
                rect.SetParent(settlementCanvas.transform, false);
                rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(-140f, 170f);
                rect.sizeDelta = new Vector2(210f, 210f);
                confirmButton = stamp.GetComponent<Button>();
                confirmStampImage = stamp.GetComponent<SettlementStampImage>();
            }
        }
        if (confirmStampImage == null) confirmStampImage = confirmButton.GetComponent<Image>();
        if (confirmStampImage != null)
        {
            confirmStampImage.raycastTarget = true;
            confirmStampImage.color = new Color(0.9f, 0.9f, 0.86f, 1f);
            confirmStampImage.preserveAspect = true;
            confirmButton.targetGraphic = confirmStampImage;
        }
        if (confirmButtonText == null)
        {
            confirmButtonText = confirmButton.GetComponentInChildren<TMP_Text>(true);
            if (confirmButtonText == null)
            {
                var label = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                label.layer = settlementCanvas.layer;
                RectTransform rect = label.GetComponent<RectTransform>();
                rect.SetParent(confirmButton.transform, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = new Vector2(12f, 12f);
                rect.offsetMax = new Vector2(-12f, -12f);
                confirmButtonText = label.GetComponent<TextMeshProUGUI>();
                confirmButtonText.font = returnHintText != null ? returnHintText.font : TMP_Settings.defaultFontAsset;
            }
        }
        confirmButtonText.raycastTarget = false;
        confirmButtonText.fontSize = 24f;
        confirmButtonText.color = new Color(0.15f, 0.19f, 0.16f, 1f);
        confirmButtonText.alignment = TextAlignmentOptions.Center;
        ColorBlock colors = confirmButton.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 0.94f, 0.77f, 1f);
        colors.pressedColor = new Color(0.85f, 0.73f, 0.45f, 1f);
        colors.disabledColor = new Color(0.65f, 0.65f, 0.65f, 0.8f);
        confirmButton.colors = colors;
        confirmButton.navigation = new Navigation { mode = Navigation.Mode.None };
        confirmButton.onClick.RemoveListener(OnConfirmClicked);
        confirmButton.onClick.AddListener(OnConfirmClicked);

        // Keep the two confirmation indicators below the stamp instead of behind it.
        for (int i = 0; i < confirmationTexts.Length; i++)
        {
            TMP_Text text = confirmationTexts[i];
            if (text == null) continue;
            RectTransform rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(-225f + i * 160f, 40f);
            rect.sizeDelta = new Vector2(130f, 44f);
            text.enableAutoSizing = true;
            text.fontSizeMin = 12f;
            text.fontSizeMax = 18f;
            text.raycastTarget = false;
        }
        foreach (string cardName in new[] { "Player 1 Card", "Player 2 Card" })
        {
            Transform card = settlementCanvas.transform.Find(cardName);
            if (card != null) card.gameObject.SetActive(false);
        }
        if (returnHintText != null)
        {
            RectTransform rect = returnHintText.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.anchoredPosition = new Vector2(30f, 45f);
            rect.sizeDelta = new Vector2(420f, 70f);
            returnHintText.fontSize = 20f;
            returnHintText.raycastTarget = false;
        }
        if (issueText != null)
        {
            RectTransform rect = issueText.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.anchoredPosition = new Vector2(30f, 5f);
            rect.sizeDelta = new Vector2(420f, 32f);
            issueText.fontSizeMax = 18f;
            issueText.fontSizeMin = 12f;
            issueText.raycastTarget = false;
        }
        if (EventSystem.current == null && FindFirstObjectByType<EventSystem>() == null)
        {
            var events = new GameObject("Settlement EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
    }

    private void OnConfirmClicked()
    {
        if (flow != null && flow.CanConfirmResult) flow.ConfirmResult();
    }

    private void RefreshConfirmationStamp(StageSettlementData result)
    {
        if (confirmButton == null) return;
        confirmButton.interactable = flow.CanConfirmResult;
        Sprite stamp = result.IsSuccess ? successStamp : failureStamp;
        if (confirmStampImage != null)
        {
            confirmStampImage.sprite = stamp;
            confirmStampImage.color = stamp != null ? Color.white : new Color(0.9f, 0.9f, 0.86f, 1f);
        }
        if (confirmButtonText == null) return;
        confirmButtonText.text = flow.State.Phase switch
        {
            StageReturnPhase.Returning => "복귀 중",
            StageReturnPhase.WaitingForServer => flow.IsConfirming ? "확인 전송 중" : "확인 완료",
            _ => stamp != null ? string.Empty : result.IsSuccess ? "성공\n클릭하여 확인" : "실패\n클릭하여 확인"
        };
    }

    private static string Format(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
}

