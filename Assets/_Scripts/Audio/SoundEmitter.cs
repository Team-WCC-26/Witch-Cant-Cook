using UnityEngine;

public readonly struct SoundHandle
{
    private readonly SoundManager manager;
    private readonly int id;

    internal SoundHandle(SoundManager manager, int id)
    {
        this.manager = manager;
        this.id = id;
    }

    public bool IsPlaying => manager != null && manager.IsPlaying(id);

    public void Stop()
    {
        if (manager != null) manager.Stop(id);
    }
}

[DisallowMultipleComponent]
public sealed class SoundEmitter : MonoBehaviour
{
    [SerializeField] private SoundSO sound;
    [SerializeField] private bool followTarget = true;
    private SoundHandle current;

    #region Unity Callbacks
    private void OnDisable() => Stop();
    #endregion

    public void Play()
    {
        if (!isActiveAndEnabled || sound == null) return;
        Stop();
        current = SoundManager.GetOrCreate().Play(sound, transform.position, followTarget ? transform : null);
    }

    public void Stop()
    {
        current.Stop();
        current = default;
    }
}
