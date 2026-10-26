using System.Collections;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
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
    private TMP_Text confirmLabel;
    private bool CanConfirm => flow != null && flow.State.Phase == StageReturnPhase.Settlement &&
        flow.CanConfirmResult;

    private void OnEnable()
    {
        flow = StageReturnFlow.Instance;
        flow.Changed += Refresh;
        if (returnButton != null)
        {
            returnButton.gameObject.SetActive(true);
            returnButton.onClick.AddListener(OnConfirm);
            confirmLabel = returnButton.GetComponentInChildren<TMP_Text>(true);
        }
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
        if (returnButton != null) returnButton.onClick.RemoveListener(OnConfirm);
    }
    private void OnConfirm()
    {
        if (CanConfirm) flow.ConfirmResult();
    }
    private void Update()
    {
        // Read UI confirmation directly: gameplay interaction input is blocked during settlement.
        if (Application.isFocused && CanConfirm && Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame)
            OnConfirm();
        if (returnButton != null && returnButton.interactable != CanConfirm) Refresh();
    }
    private void Refresh()
    {
        StageSettlementData result = flow.State.Settlement;
        if (result == null) return;
        if (resultText != null) resultText.text = $"게임 종료 · 스테이지 {result.StageNumber} {(result.IsSuccess ? "성공" : "실패")}";
        if (earnedText != null) earnedText.text = $"획득 황금 콩  {result.EarnedMoney.ToString("N0", CultureInfo.InvariantCulture)}";
        if (walletText != null) walletText.text = $"팀 보유 황금 콩  {result.EndMoney.ToString("N0", CultureInfo.InvariantCulture)}";
        if (returnButton != null) returnButton.interactable = CanConfirm;
        if (confirmLabel != null) confirmLabel.text = flow.IsConfirming ||
            flow.State.Phase == StageReturnPhase.WaitingForServer || flow.State.Phase == StageReturnPhase.Returning
                ? "다른 플레이어 응답 대기 중.." : "결산 확인 (F)";
        if (statusText != null) statusText.text = flow.ConfirmationIssue ?? (flow.State.Phase switch
        {
            StageReturnPhase.Returning => "로비로 돌아가는 중…",
            StageReturnPhase.WaitingForServer => "서버의 로비 이동 알림을 기다리는 중",
            _ => "결산 결과를 확인해주세요."
        });
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
