#if WCC_TEST_TOOLS
using System;
using System.Collections.Generic;
using Unity.Profiling;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-500)]
public sealed class CameraPerformanceRunner : MonoBehaviour
{
    [SerializeField] private float degreesPerSecond = 30f;
    [SerializeField] private int warmupLaps = 1;
    [SerializeField] private int measuredLaps = 10;
    [SerializeField] private float maxPlayerDrift = 0.15f;

    private const int MaxSamples = 100000;
    private readonly List<CameraPerformanceSample> samples = new();
    private readonly FrameTiming[] frameTimings = new FrameTiming[1];
    private ProfilerRecorder cameraRecorder;
    private ProfilerRecorder gcRecorder;
    private ProfilerRecorder memoryRecorder;
    private ProfilerRecorder drawCallRecorder;
    private ProfilerRecorder batchRecorder;
    private PlayerBrain player;
    private PlayerCameraController cameraController;
    private CameraOcclusionController occlusionController;
    private CinemachineBrain cameraBrain;
    private ICinemachineCamera initialVirtualCamera;
    private CameraPerformanceReport.Metadata metadata;
    private Vector3 startPlayerPos;
    private double startedAt;
    private double previousFrameAt;
    private double previousElapsed;
    private float previousYaw;
    private int previousFrame;
    private int startingScene;
    private int runNumber;
    private ulong lastTimingTimestamp;
    private float activeSpeed;
    private int activeWarmupLaps;
    private int activeMeasuredLaps;
    private FullScreenMode activeFullScreenMode;
    private bool running;
    private bool hasPreviousFrame;
    private bool frameTimingEnabled;
    private int displayedLap = -1;
    private string[] lapStatuses;
    private string status = "WCC test tools: F8 starts a stationary camera test in the lobby.";

    #region Unity Callbacks

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        if (FindFirstObjectByType<CameraPerformanceRunner>() != null) return;
        var owner = new GameObject(nameof(CameraPerformanceRunner));
        DontDestroyOnLoad(owner);
        owner.AddComponent<CameraPerformanceRunner>();
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame)
        {
            if (running) Finish("cancelled_by_user");
            else StartMeasurement();
            return;
        }
        if (!running) return;

        string issue = GetRunIssue();
        if (issue != null)
        {
            Finish(issue);
            return;
        }

        double now = Time.realtimeSinceStartupAsDouble;
        if (hasPreviousFrame)
        {
            CapturePreviousFrame(now);
            if (samples.Count >= MaxSamples)
            {
                Finish("sample_limit_reached");
                return;
            }
        }

        double elapsed = now - startedAt;
        double totalDegrees = elapsed * activeSpeed;
        if (totalDegrees >= (1 + activeWarmupLaps + activeMeasuredLaps) * 360d)
        {
            Finish("completed");
            return;
        }

        float yaw = (float)(totalDegrees % 360d);
        int lap = (int)(totalDegrees / 360d);
        if (lap != displayedLap)
        {
            displayedLap = lap;
            status = lapStatuses[lap];
        }
        cameraController.SetPerformanceRotation(yaw);
        previousFrameAt = now;
        previousElapsed = elapsed;
        previousYaw = yaw;
        previousFrame = Time.frameCount;
        hasPreviousFrame = true;
        if (frameTimingEnabled) FrameTimingManager.CaptureFrameTimings();
    }

    private void OnApplicationFocus(bool focused)
    {
        if (!focused && running) Finish("application_focus_lost");
    }

    private void OnDisable()
    {
        if (running) Finish("runner_disabled");
        DisposeRecorders();
    }

    private void OnApplicationQuit()
    {
        if (running) Finish("application_quit");
    }

    private void OnValidate()
    {
        degreesPerSecond = Mathf.Clamp(degreesPerSecond, 1f, 360f);
        warmupLaps = Mathf.Clamp(warmupLaps, 0, 20);
        measuredLaps = Mathf.Clamp(measuredLaps, 1, 100);
        maxPlayerDrift = Mathf.Max(0.01f, maxPlayerDrift);
    }

    private void OnGUI()
    {
        GUI.Label(new Rect(12f, 12f, 1200f, 26f), status);
    }

    #endregion

    private void StartMeasurement()
    {
        if (SceneManager.GetActiveScene().name != "MainScene" || !RoomHudSession.Instance.Lobby.IsActive)
        {
            status = "Enter the room lobby, choose a position, then press F8.";
            return;
        }
        PlayerSpawnManager players = PlayerSpawnManager.Instance;
        if (players == null || string.IsNullOrEmpty(players.MyID)
            || !players.TryGetPlayer(players.MyID, out player) || player == null
            || player.PlayerCam == null || !player.PlayerCam.isActiveAndEnabled
            || player.CameraController == null || !Application.isFocused
            || player.Body == null || player.Body.IsRagdoll)
        {
            status = "Local player and gameplay camera must be ready and focused.";
            return;
        }

        cameraController = player.CameraController;
        occlusionController = player.PlayerCam.GetComponent<CameraOcclusionController>();
        cameraBrain = player.PlayerCam.GetComponent<CinemachineBrain>();
        if (cameraBrain == null || cameraBrain.ActiveVirtualCamera == null || cameraBrain.IsBlending
            || occlusionController == null || !occlusionController.IsGameplayViewActive)
        {
            status = "Wait for the gameplay camera transition to finish before starting.";
            return;
        }
        if (!cameraController.BeginPerformanceRotation())
        {
            status = "Close menus and leave special camera views before starting.";
            cameraController = null;
            player = null;
            return;
        }

        try
        {
            activeSpeed = Mathf.Clamp(degreesPerSecond, 1f, 360f);
            activeWarmupLaps = Mathf.Clamp(warmupLaps, 0, 20);
            activeMeasuredLaps = Mathf.Clamp(measuredLaps, 1, 100);
            startPlayerPos = player.VisualPos;
            initialVirtualCamera = cameraBrain.ActiveVirtualCamera;
            startingScene = SceneManager.GetActiveScene().handle;
            runNumber++;
            metadata = CameraPerformanceReport.CreateMetadata(player, activeSpeed,
                activeWarmupLaps, activeMeasuredLaps, runNumber);
            samples.Clear();
            if (samples.Capacity < MaxSamples) samples.Capacity = MaxSamples;
            metadata.sampleBufferCapacity = samples.Capacity;
            metadata.sampleBufferBytes = (long)samples.Capacity
                * System.Runtime.InteropServices.Marshal.SizeOf<CameraPerformanceSample>();
            activeFullScreenMode = Screen.fullScreenMode;
            DisposeRecorders();
            cameraRecorder = StartRecorder(ProfilerCategory.Scripts, "CameraOcclusion.Update");
            gcRecorder = StartRecorder(ProfilerCategory.Memory, "GC Allocated In Frame");
            memoryRecorder = StartRecorder(ProfilerCategory.Memory, "Total Used Memory");
            drawCallRecorder = StartRecorder(ProfilerCategory.Render, "Draw Calls Count");
            batchRecorder = StartRecorder(ProfilerCategory.Render, "Batches Count");
            frameTimingEnabled = FrameTimingManager.IsFeatureEnabled();
            metadata.frameTimingEnabled = frameTimingEnabled;
            metadata.cameraRecorderAvailable = cameraRecorder.Valid;
            metadata.gcRecorderAvailable = gcRecorder.Valid;
            metadata.memoryRecorderAvailable = memoryRecorder.Valid;
            metadata.drawCallRecorderAvailable = drawCallRecorder.Valid;
            metadata.batchRecorderAvailable = batchRecorder.Valid;
            lastTimingTimestamp = 0;
            displayedLap = -1;
            lapStatuses = new string[1 + activeWarmupLaps + activeMeasuredLaps];
            for (int index = 0; index < lapStatuses.Length; index++)
            {
                string phase = index == 0 ? "initial" : index <= activeWarmupLaps ? "warmup" : "measured";
                lapStatuses[index] = $"Camera test: {phase}, lap {index + 1}/{lapStatuses.Length}. F8 cancels and saves.";
            }
            startedAt = Time.realtimeSinceStartupAsDouble;
            hasPreviousFrame = false;
            running = true;
            status = "Camera performance test running. F8 cancels and saves partial results.";
            Debug.Log("[Camera Performance] Started: " + metadata.utcStartedAt, this);
            if (metadata.vSyncCount > 0 || metadata.targetFrameRate > 0)
                Debug.LogWarning("[Camera Performance] Frame pacing is enabled; results describe the current capped configuration and do not measure uncapped hardware headroom.", this);
        }
        catch (Exception error)
        {
            running = false;
            cameraController.EndPerformanceRotation();
            DisposeRecorders();
            status = "Camera performance test could not start. See Player.log.";
            Debug.LogException(error, this);
        }
    }

    private string GetRunIssue()
    {
        if (!Application.isFocused) return "application_focus_lost";
        if (SceneManager.GetActiveScene().handle != startingScene) return "scene_changed";
        if (!RoomHudSession.Instance.Lobby.IsActive) return "left_lobby";
        if (player == null || !player.isActiveAndEnabled || cameraController == null
            || !cameraController.isActiveAndEnabled || !cameraController.IsPerformanceRotationActive)
            return "player_or_camera_unavailable";
        if (player.PlayerCam == null || !player.PlayerCam.isActiveAndEnabled
            || cameraController.IsSpecialViewActive || StageReturnFlow.IsSettlementActive)
            return "special_camera_or_transition";
        if (cameraBrain == null || cameraBrain.IsBlending || cameraBrain.ActiveVirtualCamera != initialVirtualCamera)
            return "virtual_camera_changed";
        if (occlusionController == null || !occlusionController.IsGameplayViewActive)
            return "occlusion_gameplay_view_unavailable";
        if ((player.VisualPos - startPlayerPos).sqrMagnitude > maxPlayerDrift * maxPlayerDrift)
            return "player_moved";
        if (Screen.width != metadata.width || Screen.height != metadata.height
            || QualitySettings.GetQualityLevel() != metadata.qualityIndex
            || QualitySettings.vSyncCount != metadata.vSyncCount
            || Application.targetFrameRate != metadata.targetFrameRate
            || Screen.fullScreenMode != activeFullScreenMode || Time.timeScale != metadata.timeScale)
            return "render_settings_changed";
        return null;
    }

    private void CapturePreviousFrame(double now)
    {
        int lap = (int)(previousElapsed * activeSpeed / 360d);
        var sample = new CameraPerformanceSample
        {
            frame = previousFrame,
            elapsed = previousElapsed,
            lap = lap,
            phase = lap == 0 ? 0 : lap <= activeWarmupLaps ? 1 : 2,
            yaw = previousYaw,
            frameMs = (now - previousFrameAt) * 1000d,
            cameraMs = ReadRecorder(cameraRecorder, 0.000001d),
            detectionMs = occlusionController != null ? occlusionController.DetectionMilliseconds : -1d,
            fadeMs = occlusionController != null ? occlusionController.FadeMilliseconds : -1d,
            preparationMs = CameraOcclusionTarget.PreparationMilliseconds,
            createdMaterials = CameraOcclusionTarget.CreatedMaterialCount,
            occludedTargets = occlusionController != null ? occlusionController.OccludedTargetCount : -1,
            assignedFadedRenderers = occlusionController != null ? occlusionController.FadedRendererCount : -1,
            gcBytes = ReadRecorder(gcRecorder),
            usedMemoryBytes = ReadRecorder(memoryRecorder),
            drawCalls = ReadRecorder(drawCallRecorder),
            batches = ReadRecorder(batchRecorder),
            cpuFrameMs = -1d,
            cpuMainMs = -1d,
            cpuPresentWaitMs = -1d,
            cpuRenderMs = -1d,
            gpuMs = -1d
        };
        if (frameTimingEnabled && FrameTimingManager.GetLatestTimings(1, frameTimings) > 0)
        {
            FrameTiming timing = frameTimings[0];
            if (timing.frameStartTimestamp != 0 && timing.frameStartTimestamp != lastTimingTimestamp)
            {
                sample.timingTimestamp = timing.frameStartTimestamp;
                sample.cpuFrameMs = ValidTime(timing.cpuFrameTime);
                sample.cpuMainMs = ValidTime(timing.cpuMainThreadFrameTime);
                sample.cpuPresentWaitMs = ValidTime(timing.cpuMainThreadPresentWaitTime, true);
                sample.cpuRenderMs = ValidTime(timing.cpuRenderThreadFrameTime);
                sample.gpuMs = ValidTime(timing.gpuFrameTime);
                lastTimingTimestamp = timing.frameStartTimestamp;
            }
        }
        samples.Add(sample);
    }

    private void Finish(string reason)
    {
        running = false;
        hasPreviousFrame = false;
        try
        {
            if (cameraController != null) cameraController.EndPerformanceRotation();
        }
        catch (Exception error)
        {
            Debug.LogException(error, this);
        }
        finally
        {
            DisposeRecorders();
            cameraController = null;
            player = null;
        }
        try
        {
            string path = CameraPerformanceReport.Save(metadata, samples, reason);
            status = "Camera test " + reason + ". Results: " + path;
            Debug.Log("[Camera Performance] " + status, this);
        }
        catch (Exception error)
        {
            status = "Camera test ended, but saving failed. See Player.log.";
            Debug.LogException(error, this);
        }
    }

    private static ProfilerRecorder StartRecorder(ProfilerCategory category, string name)
    {
        try { return ProfilerRecorder.StartNew(category, name, 1); }
        catch (ArgumentException) { return default; }
        catch (InvalidOperationException) { return default; }
    }

    private static double ReadRecorder(ProfilerRecorder recorder, double scale = 1d)
    {
        return recorder.Valid && recorder.Count > 0 ? recorder.LastValue * scale : -1d;
    }

    private static double ValidTime(double value, bool allowZero = false)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value)
            && (allowZero ? value >= 0d : value > 0d) ? value : -1d;
    }

    private void DisposeRecorders()
    {
        if (cameraRecorder.Valid) cameraRecorder.Dispose();
        if (gcRecorder.Valid) gcRecorder.Dispose();
        if (memoryRecorder.Valid) memoryRecorder.Dispose();
        if (drawCallRecorder.Valid) drawCallRecorder.Dispose();
        if (batchRecorder.Valid) batchRecorder.Dispose();
        cameraRecorder = default;
        gcRecorder = default;
        memoryRecorder = default;
        drawCallRecorder = default;
        batchRecorder = default;
    }
}
#endif
