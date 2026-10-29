using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class SoundManager : MonoBehaviour
{
    [SerializeField, Min(1)] private int maxVoices = 32;
    private static SoundManager instance;
    private readonly List<Voice> voices = new();
    private int nextId;
    private SoundHandle bgm;

    private sealed class Voice
    {
        public AudioSource Source;
        public Transform FollowTarget;
        public bool HasFollowTarget;
        public int Id;
    }

    #region Unity Callbacks
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
        if (instance == this) instance = null;
    }
    #endregion

    public static SoundManager GetOrCreate()
    {
        if (instance == null) new GameObject(nameof(SoundManager)).AddComponent<SoundManager>();
        return instance;
    }

    public SoundHandle Play(SoundSO sound, Vector3 pos, Transform followTarget = null)
    {
        if (sound == null || sound.Clip == null) return default;
        Voice voice = Acquire();
        if (voice == null) return default;
        sound.ApplyTo(voice.Source);
        voice.FollowTarget = followTarget;
        voice.HasFollowTarget = followTarget != null;
        voice.Source.transform.position = followTarget != null ? followTarget.position : pos;
        // Zero identifies an idle voice; handles retain their playback identity across pool reuse.
        nextId = nextId == int.MaxValue ? 1 : nextId + 1;
        voice.Id = nextId;
        voice.Source.Play();
        return new SoundHandle(this, voice.Id);
    }

    public void PlayBgm(SoundSO sound)
    {
        bgm.Stop();
        bgm = Play(sound, Vector3.zero);
        Voice voice = FindVoice(nextId);
        if (bgm.IsPlaying && voice != null)
        {
            voice.Source.spatialBlend = 0f;
            voice.Source.loop = true;
        }
    }

    public void StopBgm() => bgm.Stop();

    public void StopAll()
    {
        foreach (Voice voice in voices) Release(voice);
    }

    internal bool IsPlaying(int id)
    {
        Voice voice = FindVoice(id);
        return voice != null && voice.Source.isPlaying;
    }

    internal void Stop(int id)
    {
        Voice voice = FindVoice(id);
        if (voice != null) Release(voice);
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
        voice.Id = 0;
    }

    private void OnActiveSceneChanged(Scene previous, Scene next) => StopAll();
}
