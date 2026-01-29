using System;
using System.Collections.Generic;
using UnityEngine;

public interface IData
{
    int GetKey();
}

[Serializable]
public class GameData<T> where T : class, IData
{
    public List<T> data = new List<T>();
    private readonly Dictionary<int, T> dataDict = new Dictionary<int, T>();

    public void SetData(List<T> imported)
    {
        data = imported ?? new List<T>();
        dataDict.Clear();

        foreach (var item in data)
        {
            if (item == null) continue;

            int key = item.GetKey();

            if (!dataDict.TryAdd(key, item))
                Debug.LogWarning($"[GSpread] Duplicate key: {key} in {typeof(T).Name}");
        }
    }

    public T Get(int key)
    {
        if (dataDict.TryGetValue(key, out T value))
            return value;

        Debug.LogWarning($"[GSpread] Data not found: {key} in {typeof(T).Name}");
        return null;
    }

    public bool TryGet(int key, out T value) => dataDict.TryGetValue(key, out value);
    public List<T> GetAll() => data;
    public int Count => data?.Count ?? 0;
}