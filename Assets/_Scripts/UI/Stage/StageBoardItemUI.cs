using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class StageBoardItemUI : MonoBehaviour
{
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text title;
    [SerializeField] private TMP_Text description;
    private int bindingVersion;

    #region Unity Callbacks
    private void OnDisable() => ClearIcon();
    private void OnDestroy() => ClearIcon();
    #endregion

    public void Configure(Image image, TMP_Text nameLabel, TMP_Text descriptionLabel)
    {
        icon = image;
        title = nameLabel;
        description = descriptionLabel;
    }

    public void Bind(string name, string details, string iconAddress)
    {
        gameObject.SetActive(true);
        ClearIcon();
        int version = bindingVersion;
        if (title != null) title.text = name ?? "";
        if (description != null) description.text = details ?? "";
        if (icon == null) return;
        icon.sprite = null;
        icon.enabled = false;
        if (string.IsNullOrWhiteSpace(iconAddress)) return;

        SpriteCache.Assign(icon, iconAddress,
            () => this != null && bindingVersion == version && isActiveAndEnabled);
    }

    public void Clear()
    {
        ClearIcon();
        gameObject.SetActive(false);
    }

    private void ClearIcon()
    {
        bindingVersion++;
        if (icon != null) { icon.sprite = null; icon.enabled = false; }
    }
}
