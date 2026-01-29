using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;

public class GSpreadReader : Singleton<GSpreadReader> 
{
    

    [Header("Auto Init")]
    [SerializeField] private bool autoInitOnPlay = true;

    private readonly string url =
    "https://docs.google.com/spreadsheets/d/1e1XYmkXzN6dF3QCKz5fEOrEyRHAUxRJoLFlbYSwiJCc/";

    [SerializeField] private List<SheetInfo> sheets = new List<SheetInfo>();
    [SerializeField] private float timeoutSeconds = 30f;

    [NonSerialized] public bool isInit = false;

    public event Action<float, string> OnLoadProgress;
    public event Action OnLoadComplete;
    public event Action<string> OnLoadFailed;

    private async void Start()
    {
        if (!autoInitOnPlay) return;

        // Awake 순서/도메인 리로드 옵션 대응용: 1프레임 지연
        await Task.Yield();

        await Init();
    }

    public async Task<bool> Init()
    {
        if (isInit) return true;

        if (string.IsNullOrEmpty(url))
        {
            Debug.LogError("[GSpread] URL is not set");
            OnLoadFailed?.Invoke("URL이 설정되지 않았습니다.");
            return false;
        }

        int successCount = 0;

        for (int i = 0; i < sheets.Count; i++)
        {
            var sheet = sheets[i];
            float progress = sheets.Count > 0 ? (float)i / sheets.Count : 1f;

            OnLoadProgress?.Invoke(progress, $"{sheet.className} 로딩 중...");

            bool success = await LoadSheet(sheet);
            if (!success)
            {
                Debug.LogError($"[GSpread] Failed to load: {sheet.className}");
                OnLoadFailed?.Invoke($"{sheet.className} 로딩 실패");
                return false;
            }

            successCount++;
        }

        isInit = true;

        OnLoadProgress?.Invoke(1f, "완료");
        OnLoadComplete?.Invoke();

        Debug.Log($"[GSpread] Loaded {successCount}/{sheets.Count} sheets");
        return true;
    }

    public List<T> ImportData<T>() where T : class, new()
    {
        var sheet = sheets.Find(s => s.className == typeof(T).Name);

        if (sheet == null)
        {
            Debug.LogError($"[GSpread] Sheet not found for type: {typeof(T).Name}");
            return new List<T>();
        }

        if (sheet.datas == null)
        {
            Debug.LogError($"[GSpread] No data loaded for: {typeof(T).Name}");
            return new List<T>();
        }

        return ConvertToList<T>(sheet.datas);
    }

    public async Task<bool> ReloadSheet(string className)
    {
        var sheet = sheets.Find(s => s.className == className);
        if (sheet == null)
        {
            Debug.LogError($"[GSpread] Sheet not found: {className}");
            return false;
        }

        bool success = await LoadSheet(sheet);
        if (success)
        {
            isInit = true;
        }

        return success;
    }

    public async Task<bool> ReloadAll()
    {
        isInit = false;
        return await Init();
    }

    private async Task<bool> LoadSheet(SheetInfo sheet)
    {
        string baseUrl = url.Trim(); // url 끝 개행/공백 방어
        string requestUrl = $"{baseUrl}export?format=tsv&gid={sheet.sheetId}";

        using (var req = UnityWebRequest.Get(requestUrl))
        {
            req.timeout = (int)timeoutSeconds;

            var op = req.SendWebRequest();
            await op;

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[GSpread] Network error: {req.error}");
                return false;
            }

            string response = req.downloadHandler.text;
            if (string.IsNullOrEmpty(response))
            {
                Debug.LogError($"[GSpread] Empty response for {sheet.className}");
                return false;
            }

            sheet.datas = TsvToDictionary(response);
            return true;
        }
    }

    // 1행: 설명, 2행: 타입, 3행: 변수명, 4행~: 데이터
    private List<Dictionary<string, string>> TsvToDictionary(string tsv)
    {
        var list = new List<Dictionary<string, string>>();
        if (string.IsNullOrEmpty(tsv)) return list;

        var rows = tsv.Split('\n');
        if (rows.Length < 4) return list;

        // 줄 끝 \r 제거
        for (int i = 0; i < rows.Length; i++)
            rows[i] = rows[i].TrimEnd('\r');

        int descRowIndex = 0;
        int typeRowIndex = 1;
        int keyRowIndex = 2;
        int dataStartIndex = 3;

        // 안전장치: A열이 "#변수"로 시작하는 포맷인지 체크(원하면 조건 더 강화 가능)
        // 아니면 기존 방식(1행이 헤더)로 fallback 시키고 싶으면 여기서 분기하면 됨.

        var keys = rows[keyRowIndex].Split('\t', StringSplitOptions.None);

        for (int i = dataStartIndex; i < rows.Length; i++)
        {
            string row = rows[i];
            if (string.IsNullOrEmpty(row)) continue;

            var columns = row.Split('\t', StringSplitOptions.None);
            var dic = new Dictionary<string, string>(keys.Length);

            for (int j = 0; j < keys.Length; j++)
            {
                string key = keys[j];
                if (string.IsNullOrEmpty(key)) continue; // 키가 비어있으면 무시

                string value = j < columns.Length ? columns[j] : "";
                dic[key] = value;
            }

            list.Add(dic);
        }

        return list;
    }

    private List<T> ConvertToList<T>(List<Dictionary<string, string>> datas)
    {
        var list = new List<T>();

        foreach (var data in datas)
        {
            T item = ConvertToClass<T>(data);
            if (item != null)
                list.Add(item);
        }

        return list;
    }

    private T ConvertToClass<T>(Dictionary<string, string> data)
    {
        try
        {
            var instance = Activator.CreateInstance(typeof(T));
            var fields = typeof(T).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (var field in fields)
            {
                if (!data.TryGetValue(field.Name, out string value)) continue;
                if (string.IsNullOrEmpty(value)) continue;

                try
                {
                    object convertedValue = ConvertValue(value, field.FieldType);
                    if (convertedValue != null)
                        field.SetValue(instance, convertedValue);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[GSpread] Parse error - Field: {field.Name}, Value: {value}, Error: {e.Message}");
                }
            }

            return (T)instance;
        }
        catch (Exception e)
        {
            Debug.LogError($"[GSpread] Failed to create instance of {typeof(T).Name}: {e.Message}");
            return default;
        }
    }

    private object ConvertValue(string value, Type targetType)
    {
        if (string.IsNullOrEmpty(value))
        {
            if (targetType == typeof(string)) return "";
            if (targetType == typeof(int)) return 0;
            if (targetType == typeof(long)) return 0L;
            if (targetType == typeof(float)) return 0f;
            if (targetType == typeof(double)) return 0d;
            if (targetType == typeof(bool)) return false;

            if (targetType == typeof(Vector2)) return Vector2.zero;
            if (targetType == typeof(Vector3)) return Vector3.zero;

            if (targetType == typeof(List<int>)) return new List<int>();
            if (targetType == typeof(List<float>)) return new List<float>();
            if (targetType == typeof(List<string>)) return new List<string>();

            if (targetType == typeof(int[])) return Array.Empty<int>();
            if (targetType == typeof(float[])) return Array.Empty<float>();
            if (targetType == typeof(string[])) return Array.Empty<string>();

            if (targetType.IsEnum) return Enum.GetValues(targetType).GetValue(0);

            return null;
        }

        if (targetType == typeof(int))
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) ? i : 0;

        if (targetType == typeof(float))
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : 0f;

        if (targetType == typeof(double))
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0d;

        if (targetType == typeof(bool))
            return value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1";

        if (targetType == typeof(string))
            return value;

        if (targetType == typeof(long))
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l) ? l : 0L;

        if (targetType == typeof(Vector3))
            return value.ToVector3();

        if (targetType == typeof(Vector2))
            return value.ToVector2();

        if (targetType == typeof(List<int>))
            return ParseList(value, s => int.Parse(s, CultureInfo.InvariantCulture));

        if (targetType == typeof(List<float>))
            return ParseList(value, s => float.Parse(s, CultureInfo.InvariantCulture));

        if (targetType == typeof(List<string>))
            return value.Split('|', StringSplitOptions.None).ToList();

        if (targetType == typeof(int[]))
            return ParseList(value, s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray();

        if (targetType == typeof(float[]))
            return ParseList(value, s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();

        if (targetType == typeof(string[]))
            return value.Split('|', StringSplitOptions.None);

        if (targetType.IsEnum)
        {
            if (Enum.TryParse(targetType, value, true, out object enumValue))
                return enumValue;

            return Enum.GetValues(targetType).GetValue(0);
        }

        return null;
    }

    private List<T> ParseList<T>(string value, Func<string, T> parser)
    {
        var list = new List<T>();
        if (string.IsNullOrEmpty(value)) return list;

        var parts = value.Split('|', StringSplitOptions.None);
        foreach (var part in parts)
        {
            var p = part.Trim();
            if (p.Length == 0) continue;

            try { list.Add(parser(p)); }
            catch { Debug.LogWarning($"[GSpread] Failed to parse list item: {part}"); }
        }

        return list;
    }
}