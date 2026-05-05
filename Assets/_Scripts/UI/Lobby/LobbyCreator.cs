using Protocol;
using Server;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LobbyCreator : MonoBehaviour
{
    [Header("Input Field")]
    [SerializeField] private TMP_InputField _nameInput;
    [SerializeField] private TMP_InputField _passwordInput;

    [Header("Sprites")]
    [SerializeField] private Sprite _publicSprite;
    [SerializeField] private Sprite _privateSprite;

    [Header("Icon")]
    [SerializeField] private Image _icon;

    [Header("Button")]
    [SerializeField] private Button _confirmButton;

    private void OnEnable()
    {
        Init();

        _passwordInput.onValueChanged.AddListener(CheckPassword);
        _confirmButton.onClick.AddListener(CreateLobby);
    }

    private void OnDisable()
    {
        _passwordInput.onValueChanged.RemoveListener(CheckPassword);
        _confirmButton.onClick.RemoveListener(CreateLobby);
    }

    public void Init()
    {
        _nameInput.text = "";
        _passwordInput.text = "";
        _icon.sprite = _publicSprite;
    }

    public void CheckPassword(string value)
    {
        _icon.sprite = string.IsNullOrEmpty(value) ? _publicSprite : _privateSprite;
    }

    public async void CreateLobby()
    {
        CreateRoomPacket packet = new()
        {
            RoomName = _nameInput.text,
            RoomPassword = _passwordInput.text,
        };

        await ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));

        gameObject.SetActive(false);
    }
}
