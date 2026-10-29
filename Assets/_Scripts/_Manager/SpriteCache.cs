using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;

// One shared cache for all Sprite users.
public static class SpriteCache
{
    private sealed class SpriteEntry
    {
        public AsyncOperationHandle<Sprite> handle;
        public readonly TaskCompletionSource<Sprite> loadCompletion = new();
        public bool isReleased;
    }

    private static readonly Dictionary<string, SpriteEntry> entries = new(StringComparer.Ordinal);
    private static Task preloadTask;
    private static int cacheVersion;
    public static string LoadingStatus
    {
        get
        {
            int completed = 0;
            int failed = 0;
            string pending = null;
            foreach (var pair in entries)
            {
                if (pair.Value.loadCompletion.Task.IsCompleted)
                {
                    completed++;
                    if (pair.Value.isReleased) failed++;
                }
                else if (pending == null) pending = pair.Key;
            }
            string phase = preloadTask == null ? "Not started" :
                preloadTask.IsCompleted ? "Complete" : "Preloading";
            if (pending != null && pending.Length > 70) pending = pending.Substring(0, 70) + "...";
            return $"{phase} | Finished {completed}/{entries.Count}, failed {failed}" +
                (pending == null ? "" : $" | Waiting: {pending}");
        }
    }

    #region Unity Callbacks
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCache()
    {
        // Clear old state when Domain Reload is off.
        ReleaseAll();
    }
    #endregion

    #region Cache Access
    // Load all Addressable Sprites into the cache.
    public static Task PreloadAsync()
    {
        return preloadTask ??= PreloadSpritesAsync();
    }

    // Get a cached Sprite without waiting.
    public static bool TryGet(string address, out Sprite sprite)
    {
        sprite = null;
        if (string.IsNullOrWhiteSpace(address)) return false;
        if (!entries.TryGetValue(address.Trim(), out SpriteEntry entry)) return false;
        if (entry.isReleased || !entry.loadCompletion.Task.IsCompleted) return false;

        sprite = entry.loadCompletion.Task.Result;
        return sprite != null;
    }

    // Get a Sprite from the cache or wait for its load.
    public static Task<Sprite> LoadAsync(string address)
    {
        if (string.IsNullOrWhiteSpace(address)) return Task.FromResult<Sprite>(null);
        address = address.Trim();

        // Share both loaded Sprites and loads still in progress.
        if (entries.TryGetValue(address, out SpriteEntry existingEntry))
            return existingEntry.loadCompletion.Task;

        SpriteEntry entry = new();
        entries.Add(address, entry);

        try
        {
            entry.handle = Addressables.LoadAssetAsync<Sprite>(address);
            entry.handle.Completed += handle => CompleteLoad(address, entry, handle);
        }
        catch (Exception error)
        {
            Debug.LogWarning($"Sprite cache load failed: '{address}': {error.Message}");
            entry.isReleased = true;
            if (entry.handle.IsValid()) Addressables.Release(entry.handle);
            entry.loadCompletion.TrySetResult(null);
        }

        return entry.loadCompletion.Task;
    }

    // Clear the cache and free all Sprite handles.
    public static void ReleaseAll()
    {
        cacheVersion++;

        foreach (SpriteEntry entry in entries.Values)
        {
            if (entry.isReleased) continue;
            entry.isReleased = true;

            // Active loads free their handles when done.
            if (entry.loadCompletion.Task.IsCompleted && entry.handle.IsValid())
                Addressables.Release(entry.handle);
        }

        entries.Clear();
        preloadTask = null;
    }
    #endregion

    #region UI Binding
    // Load a Sprite and show it if the UI still needs it.
    public static async void Assign(Image image, string address, Func<bool> stillCurrent, TMP_Text fallback = null)
    {
        if (image == null) return;
        image.sprite = null;
        image.enabled = false;
        int version = cacheVersion;

        try
        {
            Sprite sprite = await LoadAsync(address);

            // Skip old results after a UI change or cache clear.
            if (image == null || version != cacheVersion || !stillCurrent()) return;

            image.sprite = sprite;
            image.enabled = sprite != null;
            if (sprite != null && fallback != null) fallback.text = "";
        }
        catch (Exception error)
        {
            Debug.LogException(error);
        }
    }
    #endregion

    #region Internal Loading
    private static async Task PreloadSpritesAsync()
    {
        int version = cacheVersion;

        try
        {
            var initialization = Addressables.InitializeAsync();
            await initialization.Task;
            if (version != cacheVersion) return;

            HashSet<string> addresses = GetSpriteAddresses();
            List<Task<Sprite>> loadTasks = new();

            foreach (string address in addresses)
                loadTasks.Add(LoadAsync(address));

            await Task.WhenAll(loadTasks);
        }
        catch (Exception error)
        {
            Debug.LogWarning($"Sprite cache preload failed: {error.Message}");
        }
    }

    private static HashSet<string> GetSpriteAddresses()
    {
        HashSet<string> addresses = new(StringComparer.Ordinal);

        // Names, GUIDs, and labels may point to the same Sprite.
        foreach (var locator in Addressables.ResourceLocators)
        {
            foreach (object key in locator.Keys)
            {
                if (!locator.Locate(key, typeof(Sprite), out var locations)) continue;

                foreach (var location in locations)
                    addresses.Add(location.PrimaryKey);
            }
        }

        return addresses;
    }

    private static void CompleteLoad(string address, SpriteEntry entry, AsyncOperationHandle<Sprite> handle)
    {
        Sprite sprite = !entry.isReleased && handle.Status == AsyncOperationStatus.Succeeded
            ? handle.Result
            : null;

        if (!entry.isReleased && sprite == null)
            Debug.LogWarning($"Sprite cache load failed: '{address}'. Check the Address and Sprite import type.");

        // Keep valid handles alive while the cache owns the Sprite.
        if (entry.isReleased || sprite == null)
        {
            entry.isReleased = true;
            if (handle.IsValid()) Addressables.Release(handle);
        }

        entry.loadCompletion.TrySetResult(sprite);
    }
    #endregion
}