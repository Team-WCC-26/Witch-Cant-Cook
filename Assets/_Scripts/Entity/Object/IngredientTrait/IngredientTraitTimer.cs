using UnityEngine;
using System;

public class IngredientTraitTimer
{
    private float duration;
    private float elapsed;

    private bool isRunning;
    private bool isLoop;

    private Action onComplete;

    public bool IsRunning => isRunning;
    public float Progress => duration <= 0f ? 0f : Mathf.Clamp01(elapsed / duration);

    public void StartTimer(float duration, Action callback)
    {
        this.duration = duration;
        this.onComplete = callback;

        elapsed = 0f;
        isRunning = true;
        isLoop = false;
    }

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

    /// <summary>
    /// condition이 true일 때만 시간이 흐르고,
    /// false가 되면 자동으로 초기화된다.
    /// </summary>
    public void Tick(float deltaTime, bool condition)
    {
        if (!condition)
        {
            Reset();
            return;
        }

        Tick(deltaTime);
    }

    public void TickAccurate(float deltaTime)
    {
        if (!isRunning)
            return;

        elapsed += deltaTime;

        if (elapsed < duration)
            return;

        elapsed -= duration;
        onComplete?.Invoke();
        Stop();
    }
}
