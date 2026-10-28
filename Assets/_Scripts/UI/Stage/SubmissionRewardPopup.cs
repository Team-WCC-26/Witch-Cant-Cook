using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class SubmissionRewardPopup : MonoBehaviour
{
    [SerializeField] private RectTransform panel;
    [SerializeField] private Image dishIcon;
    [SerializeField] private TMP_Text amountText;
    [SerializeField, Min(.01f)] private float slideInSeconds = .3f;
    [SerializeField, Min(.1f)] private float displaySeconds = 2f;
    [SerializeField, Min(.01f)] private float slideOutSeconds = .25f;
    private enum Phase { Idle, Loading, Entering, Holding, Exiting }
    private struct Reward { public string Icon; public int Amount; }
    private readonly Queue<Reward> pending = new();
    private Phase phase;
    private Vector2 shownPosition;
    private bool positionCached;
    private float elapsed;
    private int bindingVersion;

    private Vector2 HiddenPosition => new(panel.rect.width + 24f, shownPosition.y);

    private void OnEnable()
    {
        if (panel == null) return;
        if (!positionCached) { shownPosition = panel.anchoredPosition; positionCached = true; }
        Clear();
    }

    private void OnDisable() => Clear();

    public void Clear()
    {
        bindingVersion++;
        pending.Clear();
        phase = Phase.Idle;
        elapsed = 0f;
        if (panel != null)
        {
            if (positionCached) panel.anchoredPosition = HiddenPosition;
            panel.gameObject.SetActive(false);
        }
    }

    public void Enqueue(string iconAddress, int amount)
    {
        if (amount <= 0 || !isActiveAndEnabled) return;
        pending.Enqueue(new Reward { Icon = iconAddress, Amount = amount });
    }

    private async void BeginNext()
    {
        Reward reward = pending.Dequeue();
        phase = Phase.Loading;
        int version = ++bindingVersion;
        Sprite sprite = null;
        try { sprite = await SpriteCache.LoadAsync(reward.Icon); }
        catch (Exception error) { Debug.LogWarning($"요리 알림 아이콘을 읽지 못했습니다: {error.Message}", this); }
        if (this == null || !isActiveAndEnabled || version != bindingVersion) return;
        if (dishIcon != null) { dishIcon.sprite = sprite; dishIcon.enabled = sprite != null; }
        if (amountText != null) amountText.text = "+" + reward.Amount.ToString("N0", CultureInfo.InvariantCulture) + "콩";
        panel.anchoredPosition = HiddenPosition;
        panel.gameObject.SetActive(true);
        elapsed = 0f;
        phase = Phase.Entering;
    }

    private void Update()
    {
        if (panel == null) return;
        if (phase == Phase.Idle) { if (pending.Count > 0) BeginNext(); return; }
        if (phase == Phase.Loading) return;
        elapsed += Time.unscaledDeltaTime;
        if (phase == Phase.Entering)
        {
            float t = Mathf.Clamp01(elapsed / Mathf.Max(.01f, slideInSeconds));
            panel.anchoredPosition = Vector2.Lerp(HiddenPosition, shownPosition, 1f - Mathf.Pow(1f - t, 3f));
            if (t >= 1f) { phase = Phase.Holding; elapsed = 0f; }
        }
        else if (phase == Phase.Holding)
        {
            if (elapsed >= displaySeconds) { phase = Phase.Exiting; elapsed = 0f; }
        }
        else
        {
            float t = Mathf.Clamp01(elapsed / Mathf.Max(.01f, slideOutSeconds));
            panel.anchoredPosition = Vector2.Lerp(shownPosition, HiddenPosition, t * t * t);
            if (t >= 1f) { panel.gameObject.SetActive(false); phase = Phase.Idle; elapsed = 0f; }
        }
    }
}
