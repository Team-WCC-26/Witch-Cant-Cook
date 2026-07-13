using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;


public class UIBlind : UIBase
{
    [Header("Ink Images")]
    [SerializeField] private List<Image> inkImages = new();

    [Header("Animation")]
    [SerializeField] private float showInterval = 0.1f;
    [SerializeField] private float fadeDuration = 0.5f;

    private Coroutine effectCoroutine;
    protected override void Awake()
    {
        base.Awake();
        HideAll();
    }

    public override void Opened(params object[] param)
    {
        float duration = (float)param[0];

        if (effectCoroutine != null)
        {
            StopCoroutine(effectCoroutine);
        }

        effectCoroutine = StartCoroutine(CoBlind(duration));
    }

    private IEnumerator CoBlind(float duration)
    {
        HideAll();
        ShuffleInkImages();

        // 먹물 하나씩 나타나기
        foreach (Image image in inkImages)
        {
            image.gameObject.SetActive(true);
            SetAlpha(image, 1f);

            yield return new WaitForSeconds(showInterval);
        }

        // 유지
        yield return new WaitForSeconds(duration);

        // 동시에 Fade Out
        float elapsed = 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;

            float alpha = Mathf.Lerp(1f, 0f, elapsed / fadeDuration);

            foreach (Image image in inkImages)
            {
                SetAlpha(image, alpha);
            }

            yield return null;
        }

        HideAll();

        UIManager.Hide<UIBlind>();

        effectCoroutine = null;
    }

    private void HideAll()
    {
        foreach (Image image in inkImages)
        {
            image.gameObject.SetActive(false);
            SetAlpha(image, 0f);
        }
    }
    private void ShuffleInkImages()
    {
        for (int i = inkImages.Count - 1; i > 0; i--)
        {
            int randomIndex = Random.Range(0, i + 1);

            (inkImages[i], inkImages[randomIndex]) =
                (inkImages[randomIndex], inkImages[i]);
        }
    }
    private void SetAlpha(Image image, float alpha)
    {
        Color color = image.color;
        color.a = alpha;
        image.color = color;
    }
}