using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;

// Dedicated Sprite handles avoid ResourceManager's generic Object preload returning a Texture2D.
// All recipe views share pending requests and loaded assets; DishOrderManager owns the lifetime.
public static class RecipeSpriteLookup
{
    private sealed class Entry
    {
        public AsyncOperationHandle<Sprite> handle;
        public readonly TaskCompletionSource<Sprite> completion = new();
        public bool released;
    }
    private static readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);

    public static Task<Sprite> LoadAsync(string address)
    {
        if (string.IsNullOrWhiteSpace(address)) return Task.FromResult<Sprite>(null);
        address = address.Trim();
        if (entries.TryGetValue(address, out Entry existing)) return existing.completion.Task;
        Entry entry = new();
        entries.Add(address, entry);
        try
        {
            entry.handle = Addressables.LoadAssetAsync<Sprite>(address);
            entry.handle.Completed += handle =>
            {
                Sprite sprite = !entry.released && handle.Status == AsyncOperationStatus.Succeeded ? handle.Result : null;
                if (!entry.released && sprite == null)
                    Debug.LogWarning($"Recipe Sprite load failed: '{address}'. Check the Address and Sprite import type.");
                if (entry.released || sprite == null)
                    if (handle.IsValid()) Addressables.Release(handle);
                entry.completion.TrySetResult(sprite);
            };
        }
        catch (Exception error)
        {
            Debug.LogWarning($"Recipe Sprite load failed: '{address}': {error.Message}");
            if (entry.handle.IsValid()) Addressables.Release(entry.handle);
            entry.completion.TrySetResult(null);
        }
        return entry.completion.Task;
    }

    public static async void Assign(Image image, string address, Func<bool> stillCurrent, TMP_Text fallback = null)
    {
        if (image == null) return;
        image.sprite = null;
        image.enabled = false;
        try
        {
            Sprite sprite = await LoadAsync(address);
            if (image == null || !stillCurrent()) return;
            image.sprite = sprite;
            image.enabled = sprite != null;
            if (sprite != null && fallback != null) fallback.text = "";
        }
        catch (Exception error) { Debug.LogException(error); }
    }

    public static string MethodAddress(IngredientState method) => method switch
    {
        IngredientState.Cut => "Cutted_Icon",
        IngredientState.Grilled => "Pan_Icon",
        IngredientState.Boiled => "Pot_Icon",
        IngredientState.Roasted => "Oven_Icon",
        _ => null
    };

    public static void ReleaseAll()
    {
        foreach (Entry entry in entries.Values)
        {
            entry.released = true;
            if (entry.completion.Task.IsCompleted && entry.handle.IsValid()) Addressables.Release(entry.handle);
            // Pending loads release their own handles on completion.
        }
        entries.Clear();
    }
}
