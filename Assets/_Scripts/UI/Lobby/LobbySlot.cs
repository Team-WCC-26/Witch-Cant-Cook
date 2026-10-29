using Protocol;
using TMPro;
using UI.Slot;
using UnityEngine;
using UnityEngine.UI;

public class LobbySlot : SlotBase
{
    [SerializeField] private Image privacyBadge;
    [SerializeField] private TMP_Text privacyLabel;
    [SerializeField] private Sprite publicBadgeSprite;
    [SerializeField] private Sprite privateBadgeSprite;

    public void Init(LobbySlotData data)
    {
        base.Init(data);
        bool isPrivate = data.RoomData.BIsPrivate;
        SetIconActive(isPrivate);
        if (privacyBadge != null)
        {
            privacyBadge.sprite = isPrivate ? privateBadgeSprite : publicBadgeSprite;
            if (privacyBadge.sprite != null && privacyBadge.rectTransform.rect.height > 0f)
                privacyBadge.pixelsPerUnitMultiplier =
                    privacyBadge.sprite.rect.height / privacyBadge.rectTransform.rect.height;
        }
        if (privacyLabel != null)
        {
            privacyLabel.text = isPrivate ? "비공개" : "공개";
            Vector2 position = privacyLabel.rectTransform.anchoredPosition;
            position.x = isPrivate ? 10f : 0f;
            privacyLabel.rectTransform.anchoredPosition = position;
        }
    }
}

public class LobbySlotData : SlotDataBase
{
    public override string Name => RoomData.Name;
    public RoomData RoomData { get; set; }
}
