using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

public static class SoundSOTool
{
    private const string AudioFolder = "Assets/Addressables/Audio";
    private static readonly string[] SoundFolders = { AudioFolder + "/BGMSO", AudioFolder + "/SFXSO" };

    [MenuItem("Tools/Audio/Sync Sound SOs")]
    public static void SyncSoundSOs() => Synchronize(false);

    [MenuItem("Tools/Audio/Refresh Audio Clips")]
    public static void RefreshAudioClips() => Synchronize(true);

    [MenuItem("Tools/Audio/Sync Sound SOs", true)]
    [MenuItem("Tools/Audio/Refresh Audio Clips", true)]
    private static bool CanSynchronize() =>
        !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling;

    private static void Synchronize(bool refreshAll)
    {
        int created = 0;
        int changed = 0;
        int unmatched = 0;
        int errors = 0;
        foreach (string soundFolder in SoundFolders)
        {
            if (!AssetDatabase.IsValidFolder(soundFolder))
            {
                Debug.LogError($"[Sound SO] SO folder not found: {soundFolder}");
                return;
            }
        }

        Dictionary<string, SoundId> ids = Enum.GetValues(typeof(SoundId)).Cast<SoundId>()
            .Where(id => id != SoundId.None).ToDictionary(id => Normalize(id.ToString()));
        Dictionary<string, AudioClip> clips = new();
        Dictionary<string, string> labels = new();
        HashSet<string> duplicateClips = new();
        foreach (string label in new[] { "BGM", "SFX" })
        {
            string folder = AudioFolder + "/" + label;
            if (!AssetDatabase.IsValidFolder(folder))
            {
                Debug.LogError($"[Sound SO] Audio folder not found: {folder}");
                errors++;
                continue;
            }
            foreach (string path in FindPaths("t:AudioClip", folder))
            {
                string key = Normalize(Path.GetFileNameWithoutExtension(path));
                if (clips.ContainsKey(key))
                {
                    if (duplicateClips.Add(key))
                    {
                        Debug.LogError($"[Sound SO] Duplicate audio '{key}'; keeping {AssetDatabase.GetAssetPath(clips[key])}, ignoring {path}");
                        errors++;
                    }
                    continue;
                }
                AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null)
                {
                    Debug.LogError($"[Sound SO] Cannot load audio: {path}");
                    errors++;
                    continue;
                }
                clips.Add(key, clip);
                labels.Add(key, label);
            }
        }

        Dictionary<string, SoundSO> sounds = new();
        HashSet<string> duplicateSounds = new();
        foreach (string path in SoundFolders.SelectMany(folder => FindPaths("t:SoundSO", folder))
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            string key = Normalize(Path.GetFileNameWithoutExtension(path));
            SoundSO sound = AssetDatabase.LoadAssetAtPath<SoundSO>(path);
            if (!sounds.TryAdd(key, sound) && duplicateSounds.Add(key))
            {
                Debug.LogError($"[Sound SO] Duplicate SO '{key}'; keeping {AssetDatabase.GetAssetPath(sounds[key])}, ignoring {path}");
                errors++;
            }
        }

        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        AddressableAssetGroup group = settings != null ? settings.FindGroup("Audio") : null;
        if (group == null)
        {
            Debug.LogError("[Sound SO] Addressables Audio group not found; registration skipped.");
            errors++;
        }
        else
        {
            foreach (string label in new[] { "BGM", "SFX" })
            {
                string folder = AudioFolder + "/" + label + "SO";
                AddressableAssetEntry folderEntry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(folder), group);
                folderEntry.SetLabel("BGM", label == "BGM", true, true);
                folderEntry.SetLabel("SFX", label == "SFX", true, true);
            }
        }

        foreach (KeyValuePair<string, AudioClip> pair in clips)
        {
            try
            {
                if (!ids.TryGetValue(pair.Key, out SoundId id))
                {
                    Debug.LogError($"[Sound SO] No SoundId matches: {AssetDatabase.GetAssetPath(pair.Value)}");
                    errors++;
                    continue;
                }
                if (!sounds.TryGetValue(pair.Key, out SoundSO sound))
                {
                    if (refreshAll) continue;
                    string path = AudioFolder + "/" + labels[pair.Key] + "SO/" + Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(pair.Value)) + ".asset";
                    if (File.Exists(path) || AssetDatabase.LoadMainAssetAtPath(path) != null)
                    {
                        Debug.LogError($"[Sound SO] Asset path occupied: {path}");
                        errors++;
                        continue;
                    }
                    sound = ScriptableObject.CreateInstance<SoundSO>();
                    AssetDatabase.CreateAsset(sound, path);
                    sounds.Add(pair.Key, sound);
                    created++;
                }
                string expectedFolder = AudioFolder + "/" + labels[pair.Key] + "SO/";
                if (!AssetDatabase.GetAssetPath(sound).StartsWith(expectedFolder, StringComparison.Ordinal))
                {
                    Debug.LogError($"[Sound SO] SO is in the wrong category folder; expected {expectedFolder}, preserved {AssetDatabase.GetAssetPath(sound)}");
                    errors++;
                    continue;
                }
                SerializedObject serializedSound = new(sound);
                SerializedProperty clipProperty = serializedSound.FindProperty("clip");
                serializedSound.FindProperty("soundId").intValue = (int)id;
                if (refreshAll || clipProperty.objectReferenceValue == null)
                    clipProperty.objectReferenceValue = pair.Value;
                if (serializedSound.ApplyModifiedProperties()) changed++;

                if (group != null)
                {
                    string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(sound));
                    // Folder entries include children; preserve folder-based registration.
                    AddressableAssetEntry entry = settings.FindAssetEntry(guid);
                    if (entry != null)
                    {
                        settings.CreateOrMoveEntry(guid, group);
                        entry.SetLabel("BGM", labels[pair.Key] == "BGM", true, true);
                        entry.SetLabel("SFX", labels[pair.Key] == "SFX", true, true);
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Sound SO] Failed to process {AssetDatabase.GetAssetPath(pair.Value)}: {exception.Message}");
                errors++;
            }
        }
        foreach (KeyValuePair<string, SoundSO> pair in sounds)
        {
            if (clips.ContainsKey(pair.Key)) continue;
            Debug.LogWarning($"[Sound SO] No matching audio; SO and existing Clip preserved: {AssetDatabase.GetAssetPath(pair.Value)}");
            unmatched++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[Sound SO] {(refreshAll ? "Refresh" : "Create/repair")} complete. Created: {created}, updated: {changed}, unmatched: {unmatched}, errors: {errors}");
    }

    private static IEnumerable<string> FindPaths(string filter, string folder) =>
        AssetDatabase.FindAssets(filter, new[] { folder }).Select(AssetDatabase.GUIDToAssetPath)
            .OrderBy(path => path, StringComparer.Ordinal);

    private static string Normalize(string name) =>
        new string(name.Where(character => !char.IsWhiteSpace(character)).ToArray()).ToUpperInvariant();
}
