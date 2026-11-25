using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class FeatureRevealPopup : MonoBehaviour
{
    [SerializeField] private Image instructionImage;
    [SerializeField] private Button confirmButton;
    private TMP_Text confirmLabel;
    private string confirmText;

    internal bool IsConfigured => instructionImage != null && confirmButton != null;

    internal void Bind(Action onConfirm)
    {
        instructionImage.preserveAspect = true;
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
