using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public sealed class HumanoidAnimationGeneratorWindow : EditorWindow
{
    private readonly List<HumanoidMotionDefinition> motions = new();
    private string[] motionNames = Array.Empty<string>();
    private int selectedIndex;
    private Vector2 scrollPosition;

    [MenuItem("Tools/Player Animation/Humanoid Animation Generator")]
    private static void Open()
    {
        GetWindow<HumanoidAnimationGeneratorWindow>("Humanoid Animations");
    }

    private void OnEnable() => ReloadMotions();

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Humanoid Animation Generator", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Choose a motion, validate its muscle keys, and regenerate its AnimationClip.",
            MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Reload Motions", GUILayout.Width(120f))) ReloadMotions();
            if (GUILayout.Button("Create New Motion")) CreateHumanoidMotionWindow.Open();
        }

        EditorGUILayout.Space();
        if (motions.Count == 0)
        {
            EditorGUILayout.HelpBox("No motion definitions were found.", MessageType.Warning);
            return;
        }

        selectedIndex = Mathf.Clamp(selectedIndex, 0, motions.Count - 1);
        selectedIndex = EditorGUILayout.Popup("Motion", selectedIndex, motionNames);
        HumanoidMotionDefinition motion = motions[selectedIndex];
        DrawMotionSummary(motion);

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        IReadOnlyList<MotionValidationIssue> issues =
            HumanoidAnimationClipBuilder.Validate(motion);
        bool hasErrors = DrawValidation(issues);
        DrawCurveSummary(motion);
        EditorGUILayout.EndScrollView();

        using (new EditorGUI.DisabledScope(hasErrors))
        {
            bool exists = AssetDatabase.LoadAssetAtPath<AnimationClip>(motion.OutputPath) != null;
            if (GUILayout.Button(
                    exists ? "Regenerate AnimationClip" : "Generate AnimationClip",
                    GUILayout.Height(32f)))
            {
                HumanoidAnimationClipBuilder.TryGenerate(motion, out _);
            }
        }
    }

    private void ReloadMotions()
    {
        string selectedTypeName = motions.Count > 0 && selectedIndex < motions.Count
            ? motions[selectedIndex].GetType().FullName
            : string.Empty;

        motions.Clear();
        foreach (Type type in TypeCache.GetTypesDerivedFrom<HumanoidMotionDefinition>())
        {
            if (type.IsAbstract || type.GetConstructor(Type.EmptyTypes) == null) continue;
            try
            {
                motions.Add((HumanoidMotionDefinition)Activator.CreateInstance(type));
            }
            catch (Exception exception)
            {
                Debug.LogError($"Could not load motion '{type.FullName}': {exception.Message}");
            }
        }

        motions.Sort((left, right) =>
            string.Compare(left.DisplayName, right.DisplayName, StringComparison.Ordinal));

        motionNames = new string[motions.Count];
        selectedIndex = 0;
        for (int i = 0; i < motions.Count; i++)
        {
            motionNames[i] = motions[i].DisplayName;
            if (motions[i].GetType().FullName == selectedTypeName) selectedIndex = i;
        }
        Repaint();
    }

    private static void DrawMotionSummary(HumanoidMotionDefinition motion)
    {
        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.TextField("Clip", motion.ClipName);
            EditorGUILayout.TextField("Output", motion.OutputPath);
            EditorGUILayout.FloatField("Duration", motion.Duration);
            EditorGUILayout.FloatField("Frame Rate", motion.FrameRate);
            EditorGUILayout.Toggle("Loop", motion.Loop);
        }
    }

    private static bool DrawValidation(IReadOnlyList<MotionValidationIssue> issues)
    {
        bool hasErrors = false;
        if (issues.Count == 0)
        {
            EditorGUILayout.HelpBox("Validation passed.", MessageType.Info);
            return false;
        }

        foreach (MotionValidationIssue issue in issues)
        {
            bool isError = issue.Severity == MotionValidationSeverity.Error;
            hasErrors |= isError;
            EditorGUILayout.HelpBox(
                issue.Message,
                isError ? MessageType.Error : MessageType.Warning);
        }
        return hasErrors;
    }

    private static void DrawCurveSummary(HumanoidMotionDefinition motion)
    {
        if (motion.Curves == null) return;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"Muscle Curves ({motion.Curves.Count})", EditorStyles.boldLabel);
        foreach (HumanoidMuscleCurve curve in motion.Curves)
        {
            int keyCount = curve?.Keys?.Count ?? 0;
            EditorGUILayout.LabelField(curve?.MuscleName ?? "<null>", $"{keyCount} keys");
        }
    }
}

public sealed class CreateHumanoidMotionWindow : EditorWindow
{
    private const string MotionScriptFolder = "Assets/_Scripts/Editor/Motions";

    private string displayName = "New Motion";
    private string className = "NewMotionDefinition";
    private string clipName = "NewMotion";
    private string outputFolder = "Assets/Animations/Player/Upper/Generated";

    public static void Open()
    {
        CreateHumanoidMotionWindow window =
            GetWindow<CreateHumanoidMotionWindow>(true, "Create Humanoid Motion");
        window.minSize = new Vector2(430f, 210f);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Create Motion Definition", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Creates a compile-safe C# template. Add muscle curves to it, then reload the generator window.",
            MessageType.Info);

        displayName = EditorGUILayout.TextField("Display Name", displayName);
        className = EditorGUILayout.TextField("Class Name", className);
        clipName = EditorGUILayout.TextField("Clip Name", clipName);
        outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);

        string error = GetValidationError();
        if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);

        using (new EditorGUI.DisabledScope(!string.IsNullOrEmpty(error)))
        {
            if (GUILayout.Button("Create Motion Script", GUILayout.Height(28f))) CreateScript();
        }
    }

    private string GetValidationError()
    {
        if (string.IsNullOrWhiteSpace(displayName)) return "Display name is required.";
        if (!IsValidIdentifier(className)) return "Class name must be a valid C# identifier.";
        if (string.IsNullOrWhiteSpace(clipName)
            || clipName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return "Clip name is invalid.";
        if (string.IsNullOrWhiteSpace(outputFolder)
            || !outputFolder.StartsWith("Assets/", StringComparison.Ordinal)
            || outputFolder.EndsWith("/", StringComparison.Ordinal))
            return "Output folder must be an Assets/... path without a trailing slash.";
        if (File.Exists($"{MotionScriptFolder}/{className}.cs"))
            return "A motion script with this class name already exists.";
        return string.Empty;
    }

    private void CreateScript()
    {
        EnsureMotionScriptFolder();
        string scriptPath = $"{MotionScriptFolder}/{className}.cs";
        File.WriteAllText(scriptPath, BuildTemplate(), new UTF8Encoding(false));
        AssetDatabase.Refresh();
        Debug.Log($"Created Humanoid motion template: {scriptPath}");
        Close();
    }

    private string BuildTemplate()
    {
        string safeDisplayName = EscapeString(displayName.Trim());
        string safeClipName = EscapeString(clipName.Trim());
        string safeOutputFolder = EscapeString(outputFolder.Trim());
        StringBuilder builder = new();
        builder.AppendLine("using System.Collections.Generic;");
        builder.AppendLine();
        builder.AppendLine($"public sealed class {className} : HumanoidMotionDefinition");
        builder.AppendLine("{");
        builder.AppendLine($"    public override string DisplayName => \"{safeDisplayName}\";");
        builder.AppendLine($"    public override string ClipName => \"{safeClipName}\";");
        builder.AppendLine($"    public override string OutputFolder => \"{safeOutputFolder}\";");
        builder.AppendLine("    public override float Duration => 1f;");
        builder.AppendLine();
        builder.AppendLine("    // Add the Humanoid muscle curves for this motion here.");
        builder.AppendLine("    private static readonly HumanoidMuscleCurve[] MotionCurves =");
        builder.AppendLine("    {");
        builder.AppendLine("        // Muscle(\"Right Arm Down-Up\", Key(0f, 0f), Key(1f, 0f)),");
        builder.AppendLine("    };");
        builder.AppendLine();
        builder.AppendLine("    public override IReadOnlyList<HumanoidMuscleCurve> Curves => MotionCurves;");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void EnsureMotionScriptFolder()
    {
        const string editorFolder = "Assets/_Scripts/Editor";
        if (!AssetDatabase.IsValidFolder(MotionScriptFolder))
            AssetDatabase.CreateFolder(editorFolder, "Motions");
    }

    private static bool IsValidIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (!(char.IsLetter(value[0]) || value[0] == '_')) return false;
        for (int i = 1; i < value.Length; i++)
        {
            if (!(char.IsLetterOrDigit(value[i]) || value[i] == '_')) return false;
        }
        return true;
    }

    private static string EscapeString(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
