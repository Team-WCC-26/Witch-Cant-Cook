using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
[DisallowMultipleComponent]
public sealed class FeatureRevealPopup : MonoBehaviour
{
    [SerializeField] private RectTransform referenceLayout;
    [SerializeField] private Image instructionImage;
    [SerializeField] private Button confirmButton;
    private DrivenRectTransformTracker layoutTracker;
    private TMP_Text confirmLabel;
    private string confirmText;

    internal bool IsConfigured => instructionImage != null && confirmButton != null;

    private void OnEnable()
    {
        if (referenceLayout == null) return;
        layoutTracker.Add(this, referenceLayout, DrivenTransformProperties.Scale);
        FitReferenceLayout();
    }

    private void OnDisable() => layoutTracker.Clear();

    private void OnRectTransformDimensionsChange() => FitReferenceLayout();

    private void FitReferenceLayout()
    {
        if (!isActiveAndEnabled || referenceLayout == null) return;

        Vector2 viewportSize = ((RectTransform)transform).rect.size;
        Vector2 layoutSize = referenceLayout.rect.size;
        if (viewportSize.x <= 0f || viewportSize.y <= 0f || layoutSize.x <= 0f || layoutSize.y <= 0f) return;

        float scale = Mathf.Min(viewportSize.x / layoutSize.x, viewportSize.y / layoutSize.y);
        Vector3 fittedScale = Vector3.one * scale;
        if (referenceLayout.localScale != fittedScale) referenceLayout.localScale = fittedScale;
    }

    internal void Bind(Action onConfirm)
    {
        confirmLabel = confirmButton.GetComponentInChildren<TMP_Text>(true);
        if (confirmLabel != null) confirmText = confirmLabel.text;
        confirmButton.onClick.AddListener(() => onConfirm());
    }

    internal void Show(Sprite image)
    {
        SetWaiting(false);
        instructionImage.sprite = image;
        instructionImage.enabled = image != null;
        gameObject.SetActive(true);
    }

    internal void SetWaiting(bool waiting)
    {
        confirmButton.interactable = !waiting;
        if (confirmLabel != null)
            confirmLabel.text = waiting ? "상대의 응답을 기다리는 중.." : confirmText;
    }
}
