using System.Collections;
using UnityEngine;

[RequireComponent(typeof(CanvasGroup), typeof(Canvas))]
public sealed class DemoCompleteUI : MonoBehaviour
{
    [SerializeField] private CanvasGroup fadeGroup;
    [SerializeField, Min(0f)] private float fadeDuration = 1f;

    private RoomHudSession session;
    private StageSettlementData pendingSettlement;
    private Coroutine fadeRoutine;
    private bool shown;

    private void OnEnable()
    {
        // Override sorting after the prefab has become a child of the main Canvas.
        GetComponent<Canvas>().overrideSorting = true;
        if (fadeGroup == null) fadeGroup = GetComponent<CanvasGroup>();
        ResetPresentation();
        session = RoomHudSession.Instance;
        session.SettlementRecorded += OnSettlement;
        session.RoomJoined += ResetPresentation;
        session.RoomLeft += ResetPresentation;
        OnSettlement(session.State.LatestSettlement);
    }

    private void OnDisable()
    {
        if (session != null)
        {
            session.SettlementRecorded -= OnSettlement;
            session.RoomJoined -= ResetPresentation;
            session.RoomLeft -= ResetPresentation;
        }
        session = null;
        ResetPresentation();
    }

    private void Update()
    {
        if (pendingSettlement != null) OnSettlement(pendingSettlement);
    }

    private void OnSettlement(StageSettlementData result)
    {
        if (shown || result == null || !result.IsSuccess) return;
        DataManager data = DataManager.Instance;
        if (data == null || !data.IsDataLoaded)
        {
            pendingSettlement = result;
            return;
        }

        pendingSettlement = null;
        int finalStage = 0;
        foreach (Stage stage in data.GetStage().GetAll())
            if (stage != null && stage.stageNum > finalStage) finalStage = stage.stageNum;
        if (finalStage > 0 && result.StageNumber == finalStage) Show();
    }

    [ContextMenu("Show Demo Complete")]
    public void Show()
    {
        if (shown) return;
        if (fadeGroup == null) fadeGroup = GetComponent<CanvasGroup>();
        shown = true;
        pendingSettlement = null;
        transform.SetAsLastSibling();
        fadeGroup.blocksRaycasts = true;
        fadeRoutine = StartCoroutine(FadeToBlack());
    }

    private IEnumerator FadeToBlack()
    {
        fadeGroup.alpha = 0f;
        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            fadeGroup.alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / fadeDuration));
            yield return null;
        }
        fadeGroup.alpha = 1f;
        fadeRoutine = null;
    }

    private void ResetPresentation()
    {
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = null;
        pendingSettlement = null;
        shown = false;
        if (fadeGroup == null) return;
        fadeGroup.alpha = 0f;
        fadeGroup.blocksRaycasts = false;
        fadeGroup.interactable = false;
    }
}
