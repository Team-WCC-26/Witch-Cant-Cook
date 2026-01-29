using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

public class DataManager : Singleton<DataManager>
{
    [Header("Source")]
    [SerializeField] private bool useGSpread = true;

    [Tooltip("씬에 붙어있는 GSpreadReader 컴포넌트 할당(UseGSpread=true일 때만 필요)")]
    [SerializeField] private GSpreadReader gspreadReader;

    [Header("Lifecycle")]
    [SerializeField] private bool destroyGSpreadReaderAfterInit = true;

    [Header("Data Fields")]
    [SerializeField] private GameData<Ingredient_attribute> ingredientAttributes;

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
            if (gspreadReader == null)
            {
                Debug.LogError("[DataManager] gspreadReader is not assigned.");
                yield break;
            }

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
                Debug.LogError("[DataManager] GSpread Init failed.");
                yield break;
            }

            // GSpread 결과 -> GameData<T> 바인딩 + Resources Json 저장(에디터)
            if (!TryBindAllGameData_FromGSpread(progressTextCallback, progressValueCallback))
            {
                Debug.LogError("[DataManager] Bind from GSpread failed.");
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
                Debug.LogError("[DataManager] Bind from Resources Json failed.");
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
            Debug.LogError("[DataManager] GSpreadReader.ImportData<T>() not found.");
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
                Debug.LogError($"[DataManager] GameData field is null: {field.Name}");
                return false;
            }

            // 1) GSpreadReader.Instance.ImportData<T>() 호출
            object listObj = importMi.MakeGenericMethod(dataType).Invoke(GSpreadReader.Instance, null); // List<T>

            // 2) GameData<T>.SetData(List<T>) 호출
            MethodInfo setDataMi = field.FieldType.GetMethod("SetData", BindingFlags.Public | BindingFlags.Instance);
            if (setDataMi == null)
            {
                Debug.LogError($"[DataManager] SetData(List<T>) not found on {field.FieldType.Name}. field={field.Name}");
                return false;
            }

            setDataMi.Invoke(gameDataObj, new object[] { listObj });

            // 3) Resources Json 저장(에디터 전용)
#if UNITY_EDITOR
            try
            {
                Debug.Log($"[DataManager] Saving json: {dataType.Name}");

                // ListWrapper<T>(List<T>)
                Type wrapperType = typeof(ListWrapper<>).MakeGenericType(dataType);
                object wrapper = Activator.CreateInstance(wrapperType, new object[] { listObj });

                // 너가 분리한 파일의 클래스명에 맞춰 호출 이름을 통일해야 함
                // (현재 요구사항: JsonResourceIO)
                JsonResourceIO.Save(dataType.Name, wrapper);
            }
            catch (Exception e)
            {
                Debug.LogError($"[DataManager] Json save failed: {dataType.Name}. {e.Message}");
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
                Debug.LogError($"[DataManager] GameData field is null: {field.Name}");
                return false;
            }

            // 1) Resources에서 TextAsset 로드
            TextAsset asset = Resources.Load<TextAsset>($"Json/{dataType.Name}");
            if (asset == null)
            {
                Debug.LogError($"[DataManager] Resource Json not found: Resources/Json/{dataType.Name}.json");
                return false;
            }

            // 2) ListWrapper<T>로 역직렬화
            Type wrapperType = typeof(ListWrapper<>).MakeGenericType(dataType);
            object wrapperObj = JsonUtility.FromJson(asset.text, wrapperType);
            if (wrapperObj == null)
            {
                Debug.LogError($"[DataManager] Json deserialize failed: {dataType.Name}");
                return false;
            }

            // wrapper.list 꺼내기
            FieldInfo listField = wrapperType.GetField("list", BindingFlags.Public | BindingFlags.Instance);
            if (listField == null)
            {
                Debug.LogError($"[DataManager] Wrapper.list not found: {wrapperType.Name}");
                return false;
            }

            object listObj = listField.GetValue(wrapperObj); // List<T>

            // 3) GameData<T>.SetData(List<T>) 호출
            MethodInfo setDataMi = field.FieldType.GetMethod("SetData", BindingFlags.Public | BindingFlags.Instance);
            if (setDataMi == null)
            {
                Debug.LogError($"[DataManager] SetData(List<T>) not found on {field.FieldType.Name}. field={field.Name}");
                return false;
            }

            setDataMi.Invoke(gameDataObj, new object[] { listObj });
        }

        textCb?.Invoke("json 로드/바인딩 완료");
        valueCb?.Invoke(1f);
        return true;
    }
}
