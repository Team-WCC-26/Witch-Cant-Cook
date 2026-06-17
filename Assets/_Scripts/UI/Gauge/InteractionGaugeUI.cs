using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class InteractionGaugeUI : MonoBehaviour
{
    [SerializeField] private Image fillImage;

    private Coroutine fillCoroutine;

    private void Awake()
    {
        Hide();
    }

    public void ShowProgress(float normalizedValue)
    {
        gameObject.SetActive(true);
        SetProgress(normalizedValue);
    }

    public void StartFill(float duration, Action onComplete)
    {
        StopFill();
        gameObject.SetActive(true);
        fillCoroutine = StartCoroutine(FillRoutine(duration, onComplete));
    }

    public void StartFill(float duration)
    {
        StartFill(duration, null);
    }

    public void StopFill()
    {
        if (fillCoroutine == null) return;

        StopCoroutine(fillCoroutine);
        fillCoroutine = null;
    }

    public void Hide()
    {
        StopFill();
        SetProgress(0f);
        gameObject.SetActive(false);
    }

    private IEnumerator FillRoutine(float duration, Action onComplete)
    {
        float elapsed = 0f;
        SetProgress(0f);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            SetProgress(duration > 0f ? elapsed / duration : 1f);
            yield return null;
        }

        SetProgress(1f);
        fillCoroutine = null;
        onComplete?.Invoke();
    }

    public void SetProgress(float normalizedValue)
    {
        if (fillImage == null) return;

        fillImage.fillAmount = Mathf.Clamp01(normalizedValue);
    }
}
