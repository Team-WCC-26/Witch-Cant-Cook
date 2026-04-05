using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class StageTestKeys : MonoBehaviour
{
    public Button BTN_playground;

    private void Start()
    {
        if (BTN_playground != null)
        {
            BTN_playground.onClick.AddListener(() =>
            {
                SceneManager.LoadScene("Playground");
            });
        }
    }
    private void Update()
    {
        if (Keyboard.current.tabKey.wasPressedThisFrame)
        {
            StageManager.Instance.SkipToNext();
        }
        else if (Keyboard.current.enterKey.wasPressedThisFrame)
        {
            if (StageManager.Instance.CurrentPhase == null)
            {
                StageManager.Instance.StartGame();
            }
        }
    }


}
