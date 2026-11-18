#if WCC_TEST_TOOLS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

internal struct CameraPerformanceSample
{
    public int frame;
    public int phase;
    public int lap;
    public double elapsed;
    public float yaw;
    public double frameMs;
    public double cameraMs;
    public double detectionMs;
    public double fadeMs;
    public double preparationMs;
    public int createdMaterials;
    public int occludedTargets;
    public int assignedFadedRenderers;
    public double gcBytes;
    public double usedMemoryBytes;
    public double drawCalls;
    public double batches;
    public ulong timingTimestamp;
    public double cpuFrameMs;
    public double cpuMainMs;
    public double cpuPresentWaitMs;
    public double cpuRenderMs;
    public double gpuMs;
}

internal static class CameraPerformanceReport
{
    [Serializable]
    internal sealed class Metadata
    {
        public string utcStartedAt;
        public string applicationVersion;
        public string unityVersion;
        public string platform;
        public string operatingSystem;
        public string cpu;
        public int cpuThreads;
        public int systemMemoryMb;
        public string gpu;
        public int graphicsMemoryMb;
        public string graphicsApi;
        public string quality;
        public int qualityIndex;
        public int width;
        public int height;
        public string fullScreenMode;
        public int vSyncCount;
        public int targetFrameRate;
        public bool developmentBuild;
        public bool editor;
        public bool profilerEnabled;
        public float timeScale;
        public float degreesPerSecond;
        public int initialLaps = 1;
        public int warmupLaps;
        public int measuredLaps;
        public int sessionRunNumber;
        public Vector3 playerStartPos;
        public Vector3 cameraStartPos;
        public Vector3 cameraStartRot;
        public bool frameTimingEnabled;
        public bool cameraRecorderAvailable;
        public bool gcRecorderAvailable;
        public bool memoryRecorderAvailable;
        public bool drawCallRecorderAvailable;
        public bool batchRecorderAvailable;
        public int sampleBufferCapacity;
        public long sampleBufferBytes;
    }

    [Serializable]
    private sealed class Distribution
    {
        public int count;
        public double meanMs = -1d;
        public double p95Ms = -1d;
        public double p99Ms = -1d;
        public double maxMs = -1d;
    }

    [Serializable]
    private sealed class PhaseSummary
    {
        public string phase;
        public Distribution frame;
        public Distribution camera;
        public Distribution detection;
        public Distribution fade;
        public Distribution preparation;
        public Distribution gpu;
        public int framesAbove16_67Ms;
        public int framesAbove33_33Ms;
        public int framesAbove50Ms;
        public double firstUsedMemoryBytes = -1d;
        public double lastUsedMemoryBytes = -1d;
        public double totalGcAllocatedBytes;
    }

    [Serializable]
    private sealed class Summary
    {
        public Metadata metadata;
        public string completionReason;
        public string timingNotes = "frame_ms is Update-to-Update wall time for the previous rendered frame. ProfilerRecorder values are the latest completed frame. FrameTimingManager values are asynchronous and must not be treated as exact yaw/frame matches; timing_timestamp identifies unique timing records. Preparation is included in fade_ms, so these two must not be added. assigned_faded_renderers counts configured fading renderers, not actual draw submissions. NA denotes unsupported or unavailable metrics. Sample storage is fully reserved before the clock starts; used-memory totals include this buffer and profiler overhead. Initial lap does not reset process, material, shader, or driver caches. No hardware performance score or lower-spec FPS estimate is inferred.";
        public PhaseSummary[] phases;
    }

    /// <summary>Captures the rendering and hardware configuration before a run.</summary>
    public static Metadata CreateMetadata(PlayerBrain player, float speed, int warmupLaps,
        int measuredLaps, int runNumber)
    {
        int quality = QualitySettings.GetQualityLevel();
        return new Metadata
        {
            utcStartedAt = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            applicationVersion = Application.version,
            unityVersion = Application.unityVersion,
            platform = Application.platform.ToString(),
            operatingSystem = SystemInfo.operatingSystem,
            cpu = SystemInfo.processorType,
            cpuThreads = SystemInfo.processorCount,
            systemMemoryMb = SystemInfo.systemMemorySize,
            gpu = SystemInfo.graphicsDeviceName,
            graphicsMemoryMb = SystemInfo.graphicsMemorySize,
            graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
            quality = QualitySettings.names[quality],
            qualityIndex = quality,
            width = Screen.width,
            height = Screen.height,
            fullScreenMode = Screen.fullScreenMode.ToString(),
            vSyncCount = QualitySettings.vSyncCount,
            targetFrameRate = Application.targetFrameRate,
            developmentBuild = Debug.isDebugBuild,
            editor = Application.isEditor,
            profilerEnabled = UnityEngine.Profiling.Profiler.enabled,
            timeScale = Time.timeScale,
            degreesPerSecond = speed,
            warmupLaps = warmupLaps,
            measuredLaps = measuredLaps,
            sessionRunNumber = runNumber,
            playerStartPos = player.VisualPos,
            cameraStartPos = player.PlayerCam.transform.position,
            cameraStartRot = player.PlayerCam.transform.eulerAngles
        };
    }

    /// <summary>Saves frame samples and separate initial, warmup and measured summaries.</summary>
    public static string Save(Metadata metadata, List<CameraPerformanceSample> samples, string reason)
    {
        string folder = Path.Combine(Application.persistentDataPath, "TestResults", "CameraPerformance",
            "lobby-camera-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)
            + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(folder);
        using (var writer = new StreamWriter(Path.Combine(folder, "frames.csv"), false, new UTF8Encoding(false)))
        {
            writer.WriteLine("frame,elapsed_s,phase,lap,yaw_offset_deg,frame_ms,camera_ms,detection_ms,fade_ms,preparation_ms,created_materials,occluded_targets,assigned_faded_renderers,gc_bytes,used_memory_bytes,draw_calls,batches,timing_timestamp,async_cpu_frame_ms,async_cpu_main_ms,async_cpu_present_wait_ms,async_cpu_render_ms,async_gpu_ms");
            foreach (CameraPerformanceSample sample in samples)
            {
                writer.Write(sample.frame.ToString(CultureInfo.InvariantCulture));
                WriteNumber(writer, sample.elapsed);
                writer.Write(',');
                writer.Write(PhaseName(sample.phase));
                WriteNumber(writer, sample.lap);
                WriteNumber(writer, sample.yaw);
                WriteNumber(writer, sample.frameMs);
                WriteNumber(writer, sample.cameraMs);
                WriteNumber(writer, sample.detectionMs);
                WriteNumber(writer, sample.fadeMs);
                WriteNumber(writer, sample.preparationMs);
                WriteNumber(writer, sample.createdMaterials);
                WriteNumber(writer, sample.occludedTargets);
                WriteNumber(writer, sample.assignedFadedRenderers);
                WriteNumber(writer, sample.gcBytes);
                WriteNumber(writer, sample.usedMemoryBytes);
                WriteNumber(writer, sample.drawCalls);
                WriteNumber(writer, sample.batches);
                writer.Write(',');
                writer.Write(sample.timingTimestamp == 0 ? "NA" : sample.timingTimestamp.ToString(CultureInfo.InvariantCulture));
                WriteNumber(writer, sample.cpuFrameMs);
                WriteNumber(writer, sample.cpuMainMs);
                WriteNumber(writer, sample.cpuPresentWaitMs);
                WriteNumber(writer, sample.cpuRenderMs);
                WriteNumber(writer, sample.gpuMs);
                writer.WriteLine();
            }
        }
        var summary = new Summary
        {
            metadata = metadata,
            completionReason = reason,
            phases = new[] { Summarize(samples, 0), Summarize(samples, 1), Summarize(samples, 2) }
        };
        File.WriteAllText(Path.Combine(folder, "summary.json"), JsonUtility.ToJson(summary, true), new UTF8Encoding(false));
        return folder;
    }

    private static PhaseSummary Summarize(List<CameraPerformanceSample> samples, int phase)
    {
        var frames = new List<double>();
        var camera = new List<double>();
        var detection = new List<double>();
        var fade = new List<double>();
        var preparation = new List<double>();
        var gpu = new List<double>();
        var summary = new PhaseSummary { phase = PhaseName(phase) };
        foreach (CameraPerformanceSample sample in samples)
        {
            if (sample.phase != phase) continue;
            frames.Add(sample.frameMs);
            if (sample.cameraMs >= 0d) camera.Add(sample.cameraMs);
            if (sample.detectionMs >= 0d) detection.Add(sample.detectionMs);
            if (sample.fadeMs >= 0d) fade.Add(sample.fadeMs);
            if (sample.preparationMs >= 0d) preparation.Add(sample.preparationMs);
            if (sample.gpuMs >= 0d) gpu.Add(sample.gpuMs);
            if (sample.frameMs > 16.67d) summary.framesAbove16_67Ms++;
            if (sample.frameMs > 33.33d) summary.framesAbove33_33Ms++;
            if (sample.frameMs > 50d) summary.framesAbove50Ms++;
            if (sample.gcBytes >= 0d) summary.totalGcAllocatedBytes += sample.gcBytes;
            if (sample.usedMemoryBytes < 0d) continue;
            if (summary.firstUsedMemoryBytes < 0d) summary.firstUsedMemoryBytes = sample.usedMemoryBytes;
            summary.lastUsedMemoryBytes = sample.usedMemoryBytes;
        }
        summary.frame = CalculateDistribution(frames);
        summary.camera = CalculateDistribution(camera);
        summary.detection = CalculateDistribution(detection);
        summary.fade = CalculateDistribution(fade);
        summary.preparation = CalculateDistribution(preparation);
        summary.gpu = CalculateDistribution(gpu);
        return summary;
    }

    private static Distribution CalculateDistribution(List<double> values)
    {
        var distribution = new Distribution { count = values.Count };
        if (values.Count == 0) return distribution;
        values.Sort();
        double sum = 0d;
        foreach (double value in values) sum += value;
        distribution.meanMs = sum / values.Count;
        distribution.p95Ms = values[Math.Max(0, (int)Math.Ceiling(values.Count * 0.95d) - 1)];
        distribution.p99Ms = values[Math.Max(0, (int)Math.Ceiling(values.Count * 0.99d) - 1)];
        distribution.maxMs = values[values.Count - 1];
        return distribution;
    }

    private static string PhaseName(int phase) => phase == 0 ? "initial" : phase == 1 ? "warmup" : "measured";

    private static void WriteNumber(StreamWriter writer, double value)
    {
        writer.Write(',');
        writer.Write(value < 0d ? "NA" : value.ToString("0.######", CultureInfo.InvariantCulture));
    }
}
#endif
