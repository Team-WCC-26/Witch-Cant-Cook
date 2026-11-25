using System;
using Protocol;
using Server;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.InputSystem;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;

public sealed class CostumeMirror : MonoBehaviour
{
    private enum PartSlot
    {
        Hair,
        Hat,
        Mustache,
        Eyebrow,
        Glasses,
        Body,
        Glove,
        Backpack,
        Outerwear,
        Pants,
        Shoe,
        FullBody,
        Count
    }

    private const int PartCount = (int)PartSlot.Count;
    private static readonly string[] PartNames =
    {
        "Hair", "Hat", "Mustache", "Eyebrow", "Glasses", "Body",
        "Glove", "Backpack", "Outerwear", "Pants", "Shoe", "Full"
    };

    internal static bool IsOpen { get; private set; }
    internal static bool BlocksLobbyShortcuts => IsOpen || lastClosedFrame == Time.frameCount;
    private static int lastClosedFrame = -1;
    private readonly ushort[] slots = new ushort[PartCount];
    private readonly TMP_Text[] indices = new TMP_Text[PartCount];
    private readonly Button[,] buttons = new Button[PartCount, 2];
    private readonly RawImage[] thumbnails = new RawImage[PartCount];
    private static readonly Color CardInk = new(0.24f, 0.13f, 0.07f);
    private readonly Image[] stateIcons = new Image[PartCount];
    private Image bodySwatch;
    private readonly AsyncOperationHandle<Texture2D>[] thumbnailLoads = new AsyncOperationHandle<Texture2D>[PartCount];
    private AsyncOperationHandle<TextAsset> paletteLoad;
    private BodyPalette bodyPalette;

    [Serializable]
    private sealed class BodyPalette
    {
        public Color[] colors;
    }
    private readonly ushort[] renderedSlots = new ushort[PartCount];
    private MirrorPreviewSurface previewSurface;
    private PlayerBrain player;

    #region Unity Callbacks
    private void Update()
    {
        if (player == null || Keyboard.current?.escapeKey.wasPressedThisFrame == true)
            Close();
        else if (Keyboard.current?.enterKey.wasPressedThisFrame == true ||
                 Keyboard.current?.numpadEnterKey.wasPressedThisFrame == true)
            Confirm();
    }

    private void OnDestroy()
    {
        IsOpen = false;
        foreach (var handle in thumbnailLoads)
            if (handle.IsValid()) Addressables.Release(handle);
        if (paletteLoad.IsValid()) Addressables.Release(paletteLoad);
    }
    #endregion

    internal static void OpenFor(PlayerBrain player)
    {
        if (!IsOpen) Open(player);
    }

    private static async void Open(PlayerBrain player)
    {
        IsOpen = true;
        try
        {
            var router = FindFirstObjectByType<LobbyRouterUI>(FindObjectsInactive.Include);
            Transform parent = router != null && router.gameObject.activeInHierarchy
                ? router.transform
                : UIManager.Instance?.GetBackgroundParent();
            if (parent == null) return;
            var handle = Addressables.InstantiateAsync("CostumeMirrorUI", parent);
            await handle.Task;
            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                Addressables.Release(handle);
                return;
            }

            GameObject instance = handle.Result;
            if (player == null)
            {
                Addressables.ReleaseInstance(instance);
                return;
            }

            var mirror = instance.AddComponent<CostumeMirror>();
            try { mirror.Initialize(player); }
            catch
            {
                mirror.Close();
                throw;
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            if (FindFirstObjectByType<CostumeMirror>() == null) IsOpen = false;
        }
    }

    internal static void ApplyReceivedCostume(PlayerBrain target, CostumeData costume)
    {
        ushort[] received =
        {
            costume.Hair, costume.Hat, costume.Mustache, costume.Eyebrow,
            costume.Glasses, costume.Body, costume.Glove, costume.Backpack,
            costume.Outerwear, costume.Pants, costume.Shoe, costume.FullBody
        };
        for (int index = 0; index < received.Length; index++)
            if (received[index] > PlayerAppearanceCatalog.VariantCount(PartNames[index])) return;

        PlayerAppearanceCatalog.ApplySlots(target.Animator, PartNames, received);
    }

    private void Initialize(PlayerBrain owner)
    {
        player = owner;
        previewSurface = GetComponentInChildren<MirrorPreviewSurface>();
        Transform popup = transform.Find("Popup");
        MakeButton(popup.Find("Close"), Close);
        MakeButton(popup.Find("Confirm"), Confirm);

        for (int index = 0; index < PartNames.Length; index++)
        {
            int part = index;
            string cardName = index == (int)PartSlot.FullBody ? "FullBody" : PartNames[index];
            Transform card = popup.Find(cardName);
            slots[index] = PlayerAppearanceCatalog.ReadSlot(player.Animator, PartNames[index]);
            Transform tile = card.Find("ItemPreview");
            thumbnails[index] = CreateOverlay<RawImage>(tile, "Selected Part");
            thumbnails[index].enabled = false;
            stateIcons[index] = CreateOverlay<Image>(tile, "Part State", 0.12f);
            stateIcons[index].preserveAspect = true;
            stateIcons[index].color = CardInk;
            indices[index] = card.Find("Index").GetComponent<TMP_Text>();
            indices[index].color = CardInk;
            card.Find("Name").GetComponent<TMP_Text>().color = CardInk;
            card.Find("Enabled").gameObject.SetActive(false);
            if (index == (int)PartSlot.Body)
            {
                bodySwatch = CreateOverlay<Image>(tile, "Body Color", 0.04f);
                bodySwatch.sprite = tile.GetComponent<Image>().sprite;
                bodySwatch.preserveAspect = true;
            }
            buttons[index, 0] = MakeButton(card.Find("Previous"), () => Step(part, -1));
            buttons[index, 1] = MakeButton(card.Find("Next"), () => Step(part, 1));
        }
        paletteLoad = Addressables.LoadAssetAsync<TextAsset>("CostumeThumbnail/BodyPalette");
        paletteLoad.Completed += OnPaletteLoaded;
        Refresh();
    }

    private void OnPaletteLoaded(AsyncOperationHandle<TextAsset> handle)
    {
        if (this == null || handle.Status != AsyncOperationStatus.Succeeded) return;
        bodyPalette = JsonUtility.FromJson<BodyPalette>(handle.Result.text);
        RefreshThumbnail((int)PartSlot.Body, true);
    }

    private static T CreateOverlay<T>(Transform parent, string name, float inset = 0f) where T : Graphic
    {
        var child = new GameObject(name, typeof(RectTransform));
        child.transform.SetParent(parent, false);
        var rect = (RectTransform)child.transform;
        rect.anchorMin = Vector2.one * inset;
        rect.anchorMax = Vector2.one * (1f - inset);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        T graphic = child.AddComponent<T>();
        graphic.raycastTarget = false;
        return graphic;
    }

    private static Button MakeButton(Transform target, UnityEngine.Events.UnityAction action)
    {
        var graphic = target.GetComponent<Graphic>();
        graphic.raycastTarget = true;
        var button = target.gameObject.AddComponent<Button>();
        button.targetGraphic = graphic;
        button.onClick.AddListener(action);
        return button;
    }

    private void Step(int part, int direction)
    {
        int count = PlayerAppearanceCatalog.VariantCount(PartNames[part]);
        if (count == 0) return;
        int firstSlot = part == (int)PartSlot.Body ? 1 : 0;
        int slotCount = count - firstSlot + 1;
        slots[part] = (ushort)(firstSlot + (slots[part] - firstSlot + direction + slotCount) % slotCount);
        Refresh();
    }

    private void Refresh()
    {
        previewSurface.ApplySlots(PartNames, slots);
        for (int index = 0; index < slots.Length; index++)
        {
            int count = PlayerAppearanceCatalog.VariantCount(PartNames[index]);
            bool allowed = count > 0 && (slots[(int)PartSlot.FullBody] == 0 || index == (int)PartSlot.Body || index == (int)PartSlot.FullBody);
            indices[index].text = allowed ? $"{slots[index]} / {count}" : string.Empty;
            RefreshThumbnail(index, allowed);
            buttons[index, 0].interactable = allowed;
            buttons[index, 1].interactable = allowed;
        }
    }

    private void RefreshThumbnail(int part, bool allowed)
    {
        ushort slot = slots[part];
        var variant = allowed ? PlayerAppearanceCatalog.GetVariant(PartNames[part], slot) : null;
        Image stateIcon = stateIcons[part];
        stateIcon.sprite = allowed ? null : previewSurface.BlockedIcon;
        stateIcon.enabled = variant?.mesh == null && (allowed || stateIcon.sprite != null);
        RectTransform iconRect = stateIcon.rectTransform;
        float inset = allowed ? 0.5f : 0.12f;
        iconRect.anchorMin = Vector2.one * inset;
        iconRect.anchorMax = Vector2.one * (1f - inset);
        iconRect.sizeDelta = allowed ? new Vector2(30f, 5f) : Vector2.zero;
        if (part == (int)PartSlot.Body)
        {
            Color[] colors = bodyPalette?.colors;
            bodySwatch.enabled = variant?.mesh != null && colors != null && slot <= colors.Length;
            if (bodySwatch.enabled) bodySwatch.color = colors[slot - 1];
            thumbnails[part].enabled = false;
            return;
        }

        ushort visibleSlot = variant?.mesh != null ? slot : (ushort)0;
        if (renderedSlots[part] == visibleSlot) return;
        renderedSlots[part] = visibleSlot;
        RawImage image = thumbnails[part];
        image.texture = null;
        image.enabled = false;
        if (thumbnailLoads[part].IsValid()) Addressables.Release(thumbnailLoads[part]);
        thumbnailLoads[part] = default;
        if (visibleSlot == 0) return;

        var load = Addressables.LoadAssetAsync<Texture2D>($"CostumeThumbnail/{PartNames[part]}/{visibleSlot}");
        thumbnailLoads[part] = load;
        load.Completed += result =>
        {
            if (this == null || !result.Equals(thumbnailLoads[part]) ||
                result.Status != AsyncOperationStatus.Succeeded) return;
            image.texture = result.Result;
            image.enabled = true;
        };
    }

    private void Confirm()
    {
        if (!isActiveAndEnabled || player == null || ServerManager.Instance == null) return;
        if (slots[(int)PartSlot.FullBody] != 0)
            for (int index = 0; index < (int)PartSlot.FullBody; index++)
                if (index != (int)PartSlot.Body) slots[index] = 0;

        var packet = new CostumeChangePacket
        {
            PlayerId = player.PlayerId,
            Costume = new CostumeData(slots[(int)PartSlot.Hair], slots[(int)PartSlot.Hat], slots[(int)PartSlot.Mustache], slots[(int)PartSlot.Eyebrow], slots[(int)PartSlot.Glasses], slots[(int)PartSlot.Body],
                slots[(int)PartSlot.Glove], slots[(int)PartSlot.Backpack], slots[(int)PartSlot.Outerwear], slots[(int)PartSlot.Pants], slots[(int)PartSlot.Shoe], slots[(int)PartSlot.FullBody])
        };
        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
        Close();
    }

    private void Close()
    {
        lastClosedFrame = Time.frameCount;
        gameObject.SetActive(false);
        Addressables.ReleaseInstance(gameObject);
    }
}
