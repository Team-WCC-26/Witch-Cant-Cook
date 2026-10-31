using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

[DisallowMultipleComponent]
public sealed class SoundManager : MonoBehaviour
{
    [SerializeField, Min(1)] private int maxVoices = 32;
    private static SoundManager instance;
    private readonly List<Voice> voices = new();
    private int nextId;
    private SoundHandle bgm;
    private readonly Dictionary<SoundId, SoundSO> sounds = new();
    private readonly List<AsyncOperationHandle> loadHandles = new();
    private readonly List<PendingSound> pendingSounds = new();
    private readonly HashSet<SoundId> warnedSounds = new();
    private bool sfxLoading;

    private sealed class PendingSound
    {
        public SoundId SoundId;
        public int Id;
        public Vector3 Pos;
        public Transform FollowTarget;
        public bool HasFollowTarget;
    }

    private sealed class Voice
    {
        public AudioSource Source;
        public Transform FollowTarget;
        public bool HasFollowTarget;
        public bool IsBgm;
        public int Id;
    }

    #region Unity Callbacks
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetInstance() => instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void StartGlobalBgm()
    {
        GetOrCreate().PlayBgm(SoundId.GlobalBgm);
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
        LoadBgm();
        sfxLoading = true;
        StartCoroutine(LoadSfx());
    }

    private void LateUpdate()
    {
        if (AudioListener.pause) return;
        foreach (Voice voice in voices)
        {
            if (voice.Id == 0) continue;
            if (!voice.Source.isPlaying || (voice.HasFollowTarget &&
                (voice.FollowTarget == null || !voice.FollowTarget.gameObject.activeInHierarchy)))
            {
                Release(voice);
                continue;
            }
            if (voice.HasFollowTarget) voice.Source.transform.position = voice.FollowTarget.position;
        }
    }

    private void OnDestroy()
    {
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        StopAllCoroutines();
        StopAll();
        sounds.Clear();
        foreach (AsyncOperationHandle handle in loadHandles)
            if (handle.IsValid()) Addressables.Release(handle);
        loadHandles.Clear();
        if (instance == this) instance = null;
    }

    private void OnActiveSceneChanged(Scene previous, Scene next)
    {
        pendingSounds.Clear();
        foreach (Voice voice in voices)
            if (!voice.IsBgm) Release(voice);
    }
    #endregion

    public static SoundManager GetOrCreate()
    {
        if (instance == null) new GameObject(nameof(SoundManager)).AddComponent<SoundManager>();
        return instance;
    }

    public SoundHandle Play(SoundSO sound, Vector3 pos, Transform followTarget = null)
    {
        return sound != null ? Play(sound.Id, pos, followTarget) : default;
    }

    public SoundHandle Play(SoundId soundId, Vector3 pos = default, Transform followTarget = null)
    {
        if (soundId == SoundId.None) return default;
        if (sounds.TryGetValue(soundId, out SoundSO sound))
            return PlayLoaded(sound, pos, followTarget, NextId());
        if (sfxLoading && (int)soundId >= 200 && Enum.IsDefined(typeof(SoundId), soundId))
        {
            if (pendingSounds.Count >= maxVoices) return default;
            PendingSound pending = new()
            {
                SoundId = soundId, Id = NextId(), Pos = pos,
                FollowTarget = followTarget, HasFollowTarget = followTarget != null
            };
            pendingSounds.Add(pending);
            return new SoundHandle(this, pending.Id);
        }
        WarnUnavailable(soundId);
        return default;
    }

    public void PlayBgm(SoundId soundId)
    {
        if ((int)soundId < 100 || (int)soundId >= 200)
        {
            WarnUnavailable(soundId);
            return;
        }
        bgm.Stop();
        bgm = Play(soundId);
        Voice voice = FindVoice(nextId);
        if (bgm.IsPlaying && voice != null)
        {
            voice.Source.spatialBlend = 0f;
            voice.Source.ignoreListenerPause = true;
            voice.IsBgm = true;
        }
    }

    public void PlayBgm(SoundSO sound)
    {
        if (sound != null) PlayBgm(sound.Id);
    }

    public void StopBgm() => bgm.Stop();

    public void StopAll()
    {
        pendingSounds.Clear();
        foreach (Voice voice in voices) Release(voice);
    }

    internal bool IsPlaying(int id)
    {
        Voice voice = FindVoice(id);
        return voice != null && voice.Source.isPlaying;
    }

    internal void Stop(int id)
    {
        pendingSounds.RemoveAll(pending => pending.Id == id);
        Voice voice = FindVoice(id);
        if (voice != null) Release(voice);
    }

    private SoundHandle PlayLoaded(SoundSO sound, Vector3 pos, Transform followTarget, int id)
    {
        if (sound == null || sound.Clip == null)
        {
            if (sound != null) WarnUnavailable(sound.Id);
            return default;
        }
        Voice voice = Acquire();
        if (voice == null) return default;
        sound.ApplyTo(voice.Source);
        voice.FollowTarget = followTarget;
        voice.HasFollowTarget = followTarget != null;
        voice.Source.transform.position = followTarget != null ? followTarget.position : pos;
        voice.Id = id;
        voice.Source.Play();
        return new SoundHandle(this, voice.Id);
    }

    private int NextId()
    {
        do
        {
            nextId = nextId == int.MaxValue ? 1 : nextId + 1;
        }
        while (FindVoice(nextId) != null || pendingSounds.Exists(pending => pending.Id == nextId));
        return nextId;
    }

    private void LoadBgm()
    {
        try
        {
            AsyncOperationHandle<IList<IResourceLocation>> locations = Addressables.LoadResourceLocationsAsync("BGM", typeof(SoundSO));
            loadHandles.Add(locations);
            locations.WaitForCompletion();
            if (locations.Status != AsyncOperationStatus.Succeeded)
            {
                Debug.LogWarning("[SoundManager] BGM locations failed to load.", this);
                return;
            }
            foreach (IResourceLocation location in locations.Result.OrderBy(location => location.PrimaryKey, StringComparer.Ordinal))
            {
                AsyncOperationHandle<SoundSO> handle = Addressables.LoadAssetAsync<SoundSO>(location);
                loadHandles.Add(handle);
                handle.WaitForCompletion();
                RegisterSound(handle, location.PrimaryKey, true);
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[SoundManager] BGM loading failed: {exception.Message}", this);
        }
    }

    private IEnumerator LoadSfx()
    {
        AsyncOperationHandle<IList<IResourceLocation>> locations = Addressables.LoadResourceLocationsAsync("SFX", typeof(SoundSO));
        loadHandles.Add(locations);
        yield return locations;
        if (locations.Status == AsyncOperationStatus.Succeeded)
        {
            List<AsyncOperationHandle<SoundSO>> handles = new();
            List<string> addresses = new();
            foreach (IResourceLocation location in locations.Result.OrderBy(location => location.PrimaryKey, StringComparer.Ordinal))
            {
                AsyncOperationHandle<SoundSO> handle = Addressables.LoadAssetAsync<SoundSO>(location);
                loadHandles.Add(handle);
                handles.Add(handle);
                addresses.Add(location.PrimaryKey);
            }
            for (int index = 0; index < handles.Count; index++)
            {
                yield return handles[index];
                RegisterSound(handles[index], addresses[index], false);
            }
        }
        else Debug.LogWarning("[SoundManager] SFX locations failed to load.", this);
        sfxLoading = false;
        foreach (PendingSound pending in pendingSounds)
        {
            if (!sounds.TryGetValue(pending.SoundId, out SoundSO sound))
            {
                WarnUnavailable(pending.SoundId);
                continue;
            }
            // One-shots requested before loading are intentionally discarded.
            if (!sound.Loop || (pending.HasFollowTarget &&
                (pending.FollowTarget == null || !pending.FollowTarget.gameObject.activeInHierarchy))) continue;
            PlayLoaded(sound, pending.Pos, pending.FollowTarget, pending.Id);
        }
        pendingSounds.Clear();
    }

    private void RegisterSound(AsyncOperationHandle<SoundSO> handle, string address, bool isBgm)
    {
        if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
        {
            Debug.LogWarning($"[SoundManager] Failed to load {address}.", this);
            return;
        }
        SoundSO sound = handle.Result;
        if (sound.Id == SoundId.None) return;
        int id = (int)sound.Id;
        if (!Enum.IsDefined(typeof(SoundId), sound.Id) || (isBgm ? id < 100 || id >= 200 : id < 200))
        {
            Debug.LogError($"[SoundManager] Invalid SoundId or label: {address} ({sound.Id}).", this);
            return;
        }
        if (!sounds.TryAdd(sound.Id, sound) && warnedSounds.Add(sound.Id))
            Debug.LogError($"[SoundManager] Duplicate {sound.Id}; first address retained, ignored {address}.", this);
    }

    private void WarnUnavailable(SoundId soundId)
    {
        if (warnedSounds.Add(soundId))
            Debug.LogWarning($"[SoundManager] Sound unavailable: {soundId}; playback skipped.", this);
    }

    private Voice Acquire()
    {
        foreach (Voice voice in voices)
        {
            if (voice.Id != 0 && (voice.Source.isPlaying || AudioListener.pause)) continue;
            Release(voice);
            return voice;
        }
        if (voices.Count >= maxVoices) return null;
        GameObject sourceObj = new("Sound Voice");
        sourceObj.transform.SetParent(transform, false);
        AudioSource source = sourceObj.AddComponent<AudioSource>();
        source.playOnAwake = false;
        Voice created = new() { Source = source };
        voices.Add(created);
        return created;
    }

    private Voice FindVoice(int id)
    {
        if (id == 0) return null;
        foreach (Voice voice in voices)
            if (voice.Id == id) return voice;
        return null;
    }

    private static void Release(Voice voice)
    {
        voice.Source.Stop();
        voice.Source.clip = null;
        voice.FollowTarget = null;
        voice.HasFollowTarget = false;
        voice.IsBgm = false;
        voice.Source.ignoreListenerPause = false;
        voice.Id = 0;
    }

}
