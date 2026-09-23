using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Events;
using UnityEngine.ResourceManagement.AsyncOperations;

public class DataManager : Singleton<DataManager>
{
    [Header("Source")]
    [SerializeField] private bool useGSpread = true;

    [Tooltip("씬에 붙어있는 GSpreadReader 컴포넌트 할당(UseGSpread=true일 때만 필요)")]
    [SerializeField] private GSpreadReader gspreadReader;

    [Header("Lifecycle")]
    [SerializeField] private bool destroyGSpreadReaderAfterInit = false;

    [Header("Data Fields")]
    [SerializeField] private GameData<Ingredient> ingredient = new();
    [SerializeField] private GameData<IngredientStat> ingredientStat = new();
    [SerializeField] private GameData<Recipe> recipe = new();

    public GameData<Ingredient> GetIngredient() => ingredient;
    public GameData<IngredientStat> GetIngredientStat() => ingredientStat;
    public GameData<Recipe> GetRecipe() => recipe;

    public bool IsDataLoaded { get; private set; }

    private Coroutine initCoroutine;

    protected override void Awake()
    {
        base.Awake();
        if (Instance != this) return;

        initCoroutine = StartCoroutine(Init());
    }

    public void InitWithProgress(UnityAction<string> progressTextCallback, UnityAction<float> progressValueCallback)
    {
        if (initCoroutine != null)
            StopCoroutine(initCoroutine);

        initCoroutine = StartCoroutine(Init(progressTextCallback, progressValueCallback));
    }

    public IEnumerator Init(UnityAction<string> progressTextCallback = null, UnityAction<float> progressValueCallback = null)
    {
        if (IsDataLoaded) yield break;

        if (useGSpread)
        {
            // [핵심] 인스펙터 할당이 null이라면 씬에서 직접 찾습니다.
            if (gspreadReader == null)
            {
                gspreadReader = FindAnyObjectByType<GSpreadReader>();
            }

            if (gspreadReader == null)
            {
                Debug.LogError("[DataManager] GSpreadReader is not assigned.");
                yield break;
            }

            yield return null;

            // 이벤트 구독(콜백이 있을 때만)
            Action<float, string> progressHandler = null;
            Action completeHandler = null;
            Action<string> failHandler = null;

            if (progressTextCallback != null || progressValueCallback != null)
            {
                progressHandler = (p, msg) =>
                {
                    progressValueCallback?.Invoke(p);
                    progressTextCallback?.Invoke(msg);
                };
                gspreadReader.OnLoadProgress += progressHandler;
            }

            // Init
            Task<bool> task = gspreadReader.Init();
            while (!task.IsCompleted) yield return null;

            // 구독 해제
            if (progressHandler != null)
                gspreadReader.OnLoadProgress -= progressHandler;

            if (!task.Result)
            {
                Debug.LogError("[DataManager] GSpread initialization failed.");
                yield break;
            }

            // GSpread 결과 -> GameData<T> 바인딩 + Resources Json 저장(에디터)
            if (!TryBindAllGameData_FromGSpread(progressTextCallback, progressValueCallback))
            {
                Debug.LogError("[DataManager] Binding GSpread data failed.");
                yield break;
            }

            if (destroyGSpreadReaderAfterInit)
            {
                Destroy(gspreadReader.gameObject);
                gspreadReader = null;
            }
        }
        else
        {
            if (!TryBindAllGameData_FromResourcesJson(progressTextCallback, progressValueCallback))
            {
                Debug.LogError("[DataManager] Binding Resources JSON failed.");
                yield break;
            }
        }

        IsDataLoaded = true;
    }

    private List<FieldInfo> GetAllGameDataFields()
    {
        var fields = GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var list = new List<FieldInfo>();

        foreach (var f in fields)
        {
            var ft = f.FieldType;
            if (!ft.IsGenericType) continue;
            if (ft.GetGenericTypeDefinition() != typeof(GameData<>)) continue;

            list.Add(f);
        }

        return list;
    }

    private bool TryBindAllGameData_FromGSpread(UnityAction<string> textCb, UnityAction<float> valueCb)
    {
        var gameDataFields = GetAllGameDataFields();
        int total = gameDataFields.Count;
        if (total == 0) return true;

        MethodInfo importMi = typeof(GSpreadReader).GetMethod("ImportData", BindingFlags.Public | BindingFlags.Instance);
        if (importMi == null)
        {
            Debug.LogError("[DataManager] GSpreadReader.ImportData was not found.");
            return false;
        }

        for (int i = 0; i < total; i++)
        {
            var field = gameDataFields[i];
            Type dataType = field.FieldType.GetGenericArguments()[0]; // T

            float p = total > 0 ? (float)i / total : 1f;
            textCb?.Invoke($"{dataType.Name} 바인딩/저장 중...");
            valueCb?.Invoke(p);

            object gameDataObj = field.GetValue(this);
            if (gameDataObj == null)
            {
                Debug.LogError($"[DataManager] GameData instance is null: {dataType.Name}");
                return false;
            }

            // 1) GSpreadReader.Instance.ImportData<T>() 호출
            object listObj = importMi.MakeGenericMethod(dataType).Invoke(GSpreadReader.Instance, null); // List<T>

            // 2) GameData<T>.SetData(List<T>) 호출
            MethodInfo setDataMi = field.FieldType.GetMethod("SetData", BindingFlags.Public | BindingFlags.Instance);
            if (setDataMi == null)
            {
                Debug.LogError($"[DataManager] SetData was not found: {dataType.Name}");
                return false;
            }

            setDataMi.Invoke(gameDataObj, new object[] { listObj });

            // 3) Resources Json 저장(에디터 전용)
#if UNITY_EDITOR
            try
            {

                // ListWrapper<T>(List<T>)
                Type wrapperType = typeof(ListWrapper<>).MakeGenericType(dataType);
                object wrapper = Activator.CreateInstance(wrapperType, new object[] { listObj });

                // 너가 분리한 파일의 클래스명에 맞춰 호출 이름을 통일해야 함
                // (현재 요구사항: JsonResourceIO)
                JsonResourceIO.Save(dataType.Name, wrapper);
            }
            catch (Exception e)
            {
                Debug.LogError($"[DataManager] Failed to save JSON for {dataType.Name}: {e.Message}");
                return false;
            }
#endif
        }

        textCb?.Invoke("데이터 바인딩/저장 완료");
        valueCb?.Invoke(1f);
        return true;
    }

    private bool TryBindAllGameData_FromResourcesJson(UnityAction<string> textCb, UnityAction<float> valueCb)
    {
        var gameDataFields = GetAllGameDataFields();
        int total = gameDataFields.Count;
        if (total == 0) return true;

        for (int i = 0; i < total; i++)
        {
            var field = gameDataFields[i];
            Type dataType = field.FieldType.GetGenericArguments()[0]; // T

            float p = total > 0 ? (float)i / total : 1f;
            textCb?.Invoke($"{dataType.Name} json 로드/바인딩 중...");
            valueCb?.Invoke(p);

            object gameDataObj = field.GetValue(this);
            if (gameDataObj == null)
            {
                Debug.LogError($"[DataManager] GameData instance is null: {dataType.Name}");
                return false;
            }

            // 1) Resources에서 TextAsset 로드
            TextAsset asset = Resources.Load<TextAsset>($"Json/{dataType.Name}");
            if (asset == null)
            {
                Debug.LogError($"[DataManager] Resources JSON is missing: {dataType.Name}");
                return false;
            }

            // 2) ListWrapper<T>로 역직렬화
            Type wrapperType = typeof(ListWrapper<>).MakeGenericType(dataType);
            object wrapperObj = JsonUtility.FromJson(asset.text, wrapperType);
            if (wrapperObj == null)
            {
                Debug.LogError($"[DataManager] Failed to deserialize JSON: {dataType.Name}");
                return false;
            }

            // wrapper.list 꺼내기
            FieldInfo listField = wrapperType.GetField("list", BindingFlags.Public | BindingFlags.Instance);
            if (listField == null)
            {
                Debug.LogError($"[DataManager] JSON wrapper list field is missing: {dataType.Name}");
                return false;
            }

            object listObj = listField.GetValue(wrapperObj); // List<T>

            // 3) GameData<T>.SetData(List<T>) 호출
            MethodInfo setDataMi = field.FieldType.GetMethod("SetData", BindingFlags.Public | BindingFlags.Instance);
            if (setDataMi == null)
            {
                Debug.LogError($"[DataManager] SetData was not found: {dataType.Name}");
                return false;
            }

            setDataMi.Invoke(gameDataObj, new object[] { listObj });
        }

        textCb?.Invoke("json 로드/바인딩 완료");
        valueCb?.Invoke(1f);
        return true;
    }

    public static T ParseEnum<T>(string value, T defaultValue) where T : struct, Enum
    {
        if (string.IsNullOrEmpty(value))
        {
            return defaultValue;
        }

        if (Enum.TryParse<T>(value, true, out T result))
        {
            return result;
        }

        Debug.LogWarning($"[DataManager] Failed to parse enum {typeof(T).Name}: {value}");
        return defaultValue;
    }

}
