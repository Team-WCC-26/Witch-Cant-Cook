using TMPro;
using UnityEngine;

public class PingUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _pingText;

    public void UpdatePing(long ping)
    {
        if (_pingText != null)
        {
            _pingText.text = $"Ping: {ping} ms";
        }
    }
}