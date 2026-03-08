using UnityEngine;
using UnityEngine.InputSystem;

public class StageTestKeys : MonoBehaviour
{

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
