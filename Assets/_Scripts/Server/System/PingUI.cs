using TMPro;
using UnityEngine;

public class PingUI : MonoBehaviour
{
    public static PingUI Instance { get; private set; }

    public TextMeshProUGUI pingText;

    private void Awake()
    {
        Instance = this;
    }

    public void UpdatePing(long ping)
    {
        if (pingText != null)
        {
            pingText.text = $"Ping: {ping} ms";
        }
    }
}