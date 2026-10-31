using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class UIBase : MonoBehaviour
{
    public bool isActiveInCreated = true;
    public bool isDestroyAtClosed = true;
    public eUIPosition uiPosition;
    public UnityAction<object[]> opened;
    public UnityAction<object[]> closed;

    protected virtual void Awake()
    {
        opened += Opened;
        closed += Closed;
        BindClickSounds();
    }

    public void SetActive(bool isActive)
    {
        if (isActive) BindClickSounds();
        gameObject.SetActive(isActive);
    }

    public virtual void HideDirect() { }

    public virtual void Opened(object[] param) { }

    public virtual void Closed(object[] param) { }

    private void BindClickSounds()
    {
        foreach (Button button in GetComponentsInChildren<Button>(true))
        {
            button.onClick.RemoveListener(PlayClickSound);
            button.onClick.AddListener(PlayClickSound);
        }
    }

    private static void PlayClickSound() => SoundManager.GetOrCreate().Play(SoundId.UIButtonClick);
}
