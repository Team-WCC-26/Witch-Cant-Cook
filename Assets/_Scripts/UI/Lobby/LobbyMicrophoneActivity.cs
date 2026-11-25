using System;
using UnityEngine;

// Local input activity only. Remote voice transports report via RoomHudSession.SetPlayerSpeaking.
public sealed class LobbyMicrophoneActivity : MonoBehaviour
{
    [SerializeField] private bool monitorLocalMicrophone = true;
    [SerializeField, Min(0f)] private float inputThreshold = .01f;
    [SerializeField, Min(0f)] private float releaseDelay = .18f;
    private const int WindowSamples = 512;
    private RoomHudSession session;
    private AudioClip clip;
    private float[] samples;
    private bool ownsCapture;
    private double nextPoll;
    private double lastInput = double.NegativeInfinity;

    private void OnEnable()
    {
        session = RoomHudSession.Instance;
        lastInput = double.NegativeInfinity;
        nextPoll = 0d;
        if (!monitorLocalMicrophone || Microphone.devices.Length == 0 || Microphone.IsRecording(null)) return;
        try
        {
            clip = Microphone.Start(null, true, 1, 16000);
            ownsCapture = clip != null;
            if (ownsCapture) samples = new float[WindowSamples * clip.channels];
        }
        catch (Exception exception) { Debug.LogWarning($"Microphone activity capture unavailable: {exception.Message}", this); }
    }

    private void Update()
    {
        if (!ownsCapture || clip == null || session == null) return;
        double now = Time.unscaledTimeAsDouble;
        if (now < nextPoll) return;
        nextPoll = now + .05d;
        int position = Microphone.GetPosition(null);
        if (position < WindowSamples) return;
        if (!clip.GetData(samples, position - WindowSamples)) return;
        double energy = 0d;
        foreach (float sample in samples) energy += sample * sample;
        if (Math.Sqrt(energy / samples.Length) >= inputThreshold) lastInput = now;
        session.SetPlayerSpeaking(session.Lobby.LocalPlayerId, now - lastInput <= releaseDelay);
    }

    private void OnDisable()
    {
        if (ownsCapture) Microphone.End(null);
        ownsCapture = false;
        if (clip != null) Destroy(clip);
        clip = null;
        samples = null;
        if (session != null) session.SetPlayerSpeaking(session.Lobby.LocalPlayerId, false);
        session = null;
    }
}
