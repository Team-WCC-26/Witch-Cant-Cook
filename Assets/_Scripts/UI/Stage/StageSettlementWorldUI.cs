using System.Globalization;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

// Keep this controller active while switching its two child/sibling canvases.
// Text only: no buttons, raycasters, or pointer handlers are required.
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
        Refresh();
    }

    private void OnEnable()
    {
        originalRecipeActive = recipeCanvas != null && recipeCanvas.activeSelf;
        room = RoomHudSession.Instance;
        flow = StageReturnFlow.Instance;
        room.Changed += Refresh;
        flow.Changed += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
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
        StageSettlementCamera.EnsureForScroll(cameraCanvas);
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
            _ => "F를 누르면 로비로 복귀"
        };
        if (issueText != null) issueText.text = flow.ConfirmationIssue ?? string.Empty;
    }

    private static string Format(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
