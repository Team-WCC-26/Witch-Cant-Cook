using Protocol;
using Server;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LatencySetter : MonoBehaviour
{
    [SerializeField] private Slider _receiveDelaySlider;
    [SerializeField] private TMP_InputField _receiveDelayInput;

    [SerializeField] private Slider _sendDelaySlider;
    [SerializeField] private TMP_InputField _sendDelayInput;

    [SerializeField] private Button _confirmButton;

    private void Awake()
    {
        SetReceiveDelay(_receiveDelaySlider.value);
        SetSendDelay(_sendDelaySlider.value);
    }

    private void OnEnable()
    {
        _receiveDelaySlider.onValueChanged.AddListener(SetReceiveDelay);
        _receiveDelayInput.onEndEdit.AddListener(SetReceiveSlider);
        _sendDelaySlider.onValueChanged.AddListener(SetSendDelay);
        _sendDelayInput.onEndEdit.AddListener(SetSendSlider);
        _confirmButton.onClick.AddListener(SendLatencySetPacket);
    }

    private void OnDisable()
    {
        _receiveDelaySlider.onValueChanged.RemoveListener(SetReceiveDelay);
        _receiveDelayInput.onEndEdit.RemoveListener(SetReceiveSlider);
        _sendDelaySlider.onValueChanged.RemoveListener(SetSendDelay);
        _sendDelayInput.onEndEdit.RemoveListener(SetSendSlider);
        _confirmButton.onClick.RemoveListener(SendLatencySetPacket);
    }

    private void SendLatencySetPacket()
    {
        SetLatencyPacket packet = new()
        {
            ReceiveDelayMs = (int)(_receiveDelaySlider.value * 1000),
            SendDelayMs = (int)(_sendDelaySlider.value * 1000)
        };

        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));

        gameObject.SetActive(false);
    }

    private void SetReceiveSlider(string value)
    {
        SetReceiveDelay(float.Parse(value));
    }

    private void SetReceiveDelay(float value)
    {
        RoundValue(ref value);

        _receiveDelaySlider.SetValueWithoutNotify(value);
        _receiveDelayInput.SetTextWithoutNotify(value.ToString());
    }

    private void SetSendSlider(string value)
    {
        SetSendDelay(float.Parse(value));
    }

    private void SetSendDelay(float value)
    {
        RoundValue(ref value);

        _sendDelaySlider.SetValueWithoutNotify(value);
        _sendDelayInput.SetTextWithoutNotify(value.ToString());
    }

    private void RoundValue(ref float value) => value = Mathf.Clamp(Mathf.Round(value * 10) / 10, 0, 60);
}
