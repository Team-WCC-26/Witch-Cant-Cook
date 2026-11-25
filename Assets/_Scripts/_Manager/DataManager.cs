using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class DataManager : Singleton<DataManager>
{
    [Header("Source")]
    [SerializeField] private bool useGSpread = true;
    [SerializeField] private GSpreadReader gspreadReader;

    [Header("Lifecycle")]
    [SerializeField] private bool destroyGSpreadReaderAfterInit = false;

    [Header("Data Fields")]
    [SerializeField] private GameData<Ingredient> ingredient = new();
    [SerializeField] private GameData<IngredientStat> ingredientStat = new();
    [SerializeField] private GameData<Recipe> recipe = new();
    [SerializeField] private GameData<IngredientCombination> ingredientCombination = new();
    [SerializeField] private GameData<IngredientGroup> ingredientGroup = new();
    [SerializeField] private GameData<Stage> stageInfo = new();

    public GameData<Ingredient> GetIngredient() => ingredient;
    public GameData<IngredientStat> GetIngredientStat() => ingredientStat;
    public GameData<Recipe> GetRecipe() => recipe;
    public GameData<IngredientCombination> GetIngredientCombination() => ingredientCombination;
    public GameData<IngredientGroup> GetIngredientGroup() => ingredientGroup;
    public GameData<Stage> GetStage() => stageInfo;

    public bool IsDataLoaded { get; private set; }
    public bool HasLoadingFailed => !string.IsNullOrEmpty(LoadingError);
    public string LoadingError { get; private set; }
    public string LoadingStatus => HasLoadingFailed ? $"FAILED: {LoadingError}" :
        IsDataLoaded ? "Complete (6 tables)" :
        useGSpread && gspreadReader != null && !gspreadReader.isInit ? gspreadReader.LoadingStatus : loadingStatus;

    private string loadingStatus = "Waiting to initialize";
    private Coroutine initCoroutine;

    protected override void Awake()
    {
        base.Awake();
        if (Instance != this) return;
        initCoroutine = StartCoroutine(Init());
    }

    public void InitWithProgress(UnityAction<string> progressTextCallback, UnityAction<float> progressValueCallback)
    {
        if (initCoroutine != null) StopCoroutine(initCoroutine);
        initCoroutine = StartCoroutine(Init(progressTextCallback, progressValueCallback));
    }

    // Catch coroutine failures so the title can stop waiting and display the cause.
    public IEnumerator Init(UnityAction<string> progressTextCallback = null, UnityAction<float> progressValueCallback = null)
    {
        if (IsDataLoaded) yield break;
        LoadingError = null;
        IEnumerator routine = LoadData(progressTextCallback, progressValueCallback);
        try
        {
            while (true)
            {
                bool moved = false;
                object current = null;
                Exception failure = null;
                try
                {
                    moved = routine.MoveNext();
                    if (moved) current = routine.Current;
                }
                catch (Exception error)
                {
                    failure = error.GetBaseException();
                }

                if (failure != null)
                {
                    LoadingError = failure.Message;
                    Debug.LogError($"[DataManager] Loading failed: {LoadingError}");
                    yield break;
                }
                if (!moved) yield break;
                yield return current;
            }
        }
        finally
        {
            (routine as IDisposable)?.Dispose();
        }
    }

    private IEnumerator LoadData(UnityAction<string> textCb, UnityAction<float> valueCb)
    {
        loadingStatus = useGSpread ? "Finding Google Sheets reader" : "Loading Resources/Json tables";
        if (useGSpread)
        {
            // Prefer the configured component, including before its Awake has registered a singleton.
            if (gspreadReader == null) gspreadReader = GetComponent<GSpreadReader>();
            if (gspreadReader == null) gspreadReader = FindFirstObjectByType<GSpreadReader>();
            if (gspreadReader == null)
                throw new InvalidOperationException("GSpreadReader is missing. Add the configured DataManager prefab to the starting scene.");

            // All scene Awake calls must finish before loading the configured reader.
            yield return null;
            Action<float, string> onProgress = (progress, message) =>
            {
                textCb?.Invoke(message);
                valueCb?.Invoke(progress);
            };
            gspreadReader.OnLoadProgress += onProgress;
            try
            {
                var task = gspreadReader.Init();
                while (!task.IsCompleted) yield return null;
                if (!task.GetAwaiter().GetResult())
                    throw new InvalidOperationException(gspreadReader.LoadingStatus);
            }
            finally
            {
                if (gspreadReader != null) gspreadReader.OnLoadProgress -= onProgress;
            }
        }

        // Typed calls use the same reader that was initialized. No singleton/reflection target or AOT generic lookup.
        BindTable(ingredient, 0, textCb, valueCb);
        BindTable(ingredientStat, 1, textCb, valueCb);
        BindTable(recipe, 2, textCb, valueCb);
        BindTable(ingredientCombination, 3, textCb, valueCb);
        BindTable(ingredientGroup, 4, textCb, valueCb);
        BindTable(stageInfo, 5, textCb, valueCb);

        IsDataLoaded = true;
        loadingStatus = "Complete (6 tables)";
        textCb?.Invoke(loadingStatus);
        valueCb?.Invoke(1f);

        if (destroyGSpreadReaderAfterInit && gspreadReader != null)
        {
            // The reader may share this GameObject with DataManager.
            if (gspreadReader.gameObject == gameObject) Destroy(gspreadReader);
            else Destroy(gspreadReader.gameObject);
            gspreadReader = null;
        }
    }

    private void BindTable<T>(GameData<T> target, int index, UnityAction<string> textCb, UnityAction<float> valueCb)
        where T : class, IData, new()
    {
        string table = typeof(T).Name;
        loadingStatus = $"Binding {table} ({index + 1}/6)";
        textCb?.Invoke(loadingStatus);
        valueCb?.Invoke(index / 6f);
        List<T> rows;
        if (useGSpread)
        {
            if (gspreadReader == null)
                throw new InvalidOperationException($"Google Sheets reader was destroyed before binding {table}.");
            if (!gspreadReader.HasSheet(table))
                throw new InvalidOperationException($"Google Sheets configuration is missing {table}.");
            rows = gspreadReader.ImportData<T>();
        }
        else
        {
            TextAsset asset = Resources.Load<TextAsset>($"Json/{table}");
            if (asset == null) throw new InvalidOperationException($"Resources/Json/{table}.json is missing.");
            rows = JsonUtility.FromJson<ListWrapper<T>>(asset.text)?.list;
        }

        if (rows == null || rows.Count == 0)
            throw new InvalidOperationException($"{table} contains no rows. Check the sheet ID, headers and JSON data.");
        target.SetData(rows);
        Debug.Log($"[DataManager] {table}: {rows.Count} rows loaded.");
#if UNITY_EDITOR
        if (useGSpread) JsonResourceIO.Save(table, new ListWrapper<T>(rows));
#endif
    }

    public static T ParseEnum<T>(string value, T defaultValue) where T : struct, Enum
    {
        if (string.IsNullOrEmpty(value)) return defaultValue;
        if (Enum.TryParse<T>(value, true, out T result)) return result;
        Debug.LogWarning($"[DataManager] Failed to parse enum {typeof(T).Name}: {value}");
        return defaultValue;
    }
}