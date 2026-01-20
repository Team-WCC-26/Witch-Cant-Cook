using UnityEngine;
using UnityEngine.Events;

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
    }

    public void SetActive(bool isActive)
    {
        gameObject.SetActive(isActive);
    }

    public virtual void HideDirect() { }

    public virtual void Opened(object[] param) { }

    public virtual void Closed(object[] param) { }
}