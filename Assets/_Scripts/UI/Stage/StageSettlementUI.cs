using System.Collections;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class StageSettlementUI : UIBase
{
    [SerializeField] private TMP_Text resultText;
    [SerializeField] private TMP_Text earnedText;
    [SerializeField] private TMP_Text walletText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button returnButton;
    [SerializeField] private CanvasGroup fadeGroup;
    [SerializeField, Min(0.01f)] private float fadeDuration = 0.3f;
    private StageReturnFlow flow;

    private void OnEnable()
    {
        flow = StageReturnFlow.Instance;
        flow.Changed += Refresh;
        if (returnButton != null) returnButton.gameObject.SetActive(false);
        if (fadeGroup != null)
        {
            fadeGroup.alpha = 1f;
            fadeGroup.interactable = true;
            fadeGroup.blocksRaycasts = true;
        }
        Refresh();
    }
    private void OnDisable()
    {
        if (flow != null) flow.Changed -= Refresh;
    }
    private void Refresh()
    {
        StageSettlementData result = flow.State.Settlement;
        if (result == null) return;
        if (resultText != null) resultText.text = $"게임 종료 · 스테이지 {result.StageNumber} {(result.IsSuccess ? "성공" : "실패")}";
        if (earnedText != null) earnedText.text = $"획득 황금 콩  {result.EarnedMoney.ToString("N0", CultureInfo.InvariantCulture)}";
        if (walletText != null) walletText.text = $"팀 보유 황금 콩  {result.EndMoney.ToString("N0", CultureInfo.InvariantCulture)}";
        if (statusText != null) statusText.text = flow.State.Phase == StageReturnPhase.Returning
            ? "로비로 돌아가는 중…" : "결산 완료 · 로비 복귀 대기 중";
    }
    public IEnumerator FadeOut()
    {
        if (fadeGroup == null) yield break;
        fadeGroup.interactable = false;
        float elapsed = 0;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            fadeGroup.alpha = 1f - Mathf.Clamp01(elapsed / fadeDuration);
            yield return null;
        }
        fadeGroup.interactable = true;
    }
}
