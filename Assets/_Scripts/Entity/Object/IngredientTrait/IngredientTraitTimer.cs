using UnityEngine;
using System;

public class IngredientTraitTimer : IngredientTrait
{
    private float duration;
    private float elapsed;

    private bool isRunning;
    private bool isLoop;

    private Action onComplete;

    public bool IsRunning => isRunning;
    public float Progress => Mathf.Clamp01(elapsed / duration);

    /// <summary>
    /// duration 후 한 번 실행
    /// </summary>
    public void StartTimer(float duration, Action callback)
    {
        this.duration = duration;
        this.onComplete = callback;

        elapsed = 0f;
        isRunning = true;
        isLoop = false;
    }

    /// <summary>
    /// duration마다 반복 실행
    /// </summary>
    public void StartLoop(float duration, Action callback)
    {
        this.duration = duration;
        this.onComplete = callback;

        elapsed = 0f;
        isRunning = true;
        isLoop = true;
    }

    public void Stop()
    {
        isRunning = false;
        elapsed = 0f;
    }

    public void Reset()
    {
        elapsed = 0f;
    }

    public void Tick(float deltaTime)
    {
        if (!isRunning)
            return;

        elapsed += deltaTime;

        if (elapsed < duration)
            return;

        onComplete?.Invoke();

        if (isLoop)
        {
            elapsed = 0f;
        }
        else
        {
            Stop();
        }
    }
}
