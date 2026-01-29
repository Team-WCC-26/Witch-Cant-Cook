using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using static UnityEditor.LightingExplorerTableColumn;

public static class JsonResourceIO
{
    private const string FolderPath = "Assets/Resources/Json";

    public static void Save(string fileNameWithoutExt, object wrapperObj)
    {
#if UNITY_EDITOR
        if (string.IsNullOrEmpty(fileNameWithoutExt))
            throw new ArgumentException("fileNameWithoutExt is null/empty.");

        if (!Directory.Exists(FolderPath))
            Directory.CreateDirectory(FolderPath);

        string json = JsonUtility.ToJson(wrapperObj, true);
        string path = Path.Combine(FolderPath, $"{fileNameWithoutExt}.json");

        File.WriteAllText(path, json);

        AssetDatabase.ImportAsset(path);
        AssetDatabase.Refresh();

        Debug.Log($"[DataManager] Saving End");
#else
        // 빌드 런타임에서는 Assets/Resources에 저장 불가
        Debug.LogWarning("[JsonResourceIO] Save is editor-only. Use StreamingAssets/persistentDataPath for runtime.");
#endif
    }

    public static T Load<T>(string fileName)
    {
        TextAsset asset = Resources.Load<TextAsset>($"Json/{fileName}");
        if (asset == null)
        {
            Debug.LogError($"[Json] Resource not found: Json/{fileName}");
            return default;
        }

        return JsonUtility.FromJson<T>(asset.text);
    }
}