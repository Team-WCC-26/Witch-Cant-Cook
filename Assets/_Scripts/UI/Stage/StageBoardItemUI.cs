using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;

// Each slot owns its icon handle; recipe UI cleanup cannot unload board icons.
public class StageBoardItemUI : MonoBehaviour
{
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text title;
    [SerializeField] private TMP_Text description;
    private AsyncOperationHandle<Sprite> iconHandle;
    private bool ownsHandle;
    private int bindingVersion;

    public void Configure(Image image, TMP_Text nameLabel, TMP_Text descriptionLabel)
    {
        icon = image;
        title = nameLabel;
        description = descriptionLabel;
    }

    public void Bind(string name, string details, string iconAddress)
    {
        gameObject.SetActive(true);
        ReleaseIcon();
        int version = bindingVersion;
        if (title != null) title.text = name ?? "";
        if (description != null) description.text = details ?? "";
        if (icon == null) return;
        icon.sprite = null;
        icon.enabled = false;
        if (string.IsNullOrWhiteSpace(iconAddress)) return;

        try
        {
            iconHandle = Addressables.LoadAssetAsync<Sprite>(iconAddress.Trim());
            ownsHandle = true;
            iconHandle.Completed += handle =>
            {
                if (this == null || bindingVersion != version || icon == null) return;
                if (handle.Status == AsyncOperationStatus.Succeeded)
                {
                    icon.sprite = handle.Result;
                    icon.enabled = handle.Result != null;
                }
                else Debug.LogWarning($"Stage board icon load failed: '{iconAddress}'.", this);
            };
        }
        catch (System.Exception exception)
        {
            ReleaseIcon();
            Debug.LogException(exception, this);
        }
    }

    public void Clear()
    {
        ReleaseIcon();
        gameObject.SetActive(false);
    }

    private void OnDisable() => ReleaseIcon();
    private void OnDestroy() => ReleaseIcon();

    private void ReleaseIcon()
    {
        bindingVersion++;
        if (icon != null) { icon.sprite = null; icon.enabled = false; }
        if (ownsHandle && iconHandle.IsValid()) Addressables.Release(iconHandle);
        ownsHandle = false;
    }
}
