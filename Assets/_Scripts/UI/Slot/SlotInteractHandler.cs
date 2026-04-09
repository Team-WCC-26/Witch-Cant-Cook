using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public abstract class SlotInteractHandler : UIBase, IPointerClickHandler, IPointerMoveHandler, IPointerExitHandler
{
    protected GraphicRaycaster _raycaster;
    protected List<RaycastResult> _rrList = new();

    /// <summary> 마우스가 올라가 있는 슬롯 </summary>
    protected SlotBase _pointedSlot;

    // 더블클릭 감지용 시간 저장
    protected float clickTime = 0;

    protected override void Awake()
    {
        base.Awake();

        _raycaster = GetGraphicRaycasterFromParent();
    }

    private GraphicRaycaster GetGraphicRaycasterFromParent()
    {
        Transform parent = transform.parent;

        while (parent != null)
        {
            GraphicRaycaster raycaster = parent.GetComponent<GraphicRaycaster>();
            if (raycaster != null)
            {
                return raycaster;
            }
            parent = parent.parent;
        }

        return null; // 끝까지 탐색했지만 없으면 null 반환
    }

    protected T RaycastAndGetFirstComponent<T>(PointerEventData eventData) where T : Component
    {
        _rrList.Clear();
        _raycaster.Raycast(eventData, _rrList);

        if (_rrList.Count == 0) return null;

        T component = _rrList[0].gameObject.GetComponent<T>();

        if (component == null)
        {
            component = _rrList[0].gameObject.transform.parent.GetComponent<T>();
        }

        return component;
    }

    /// <summary> 더블클릭 </summary>
    public abstract void OnDoubleClick();
    /// <summary> 좌클릭 </summary>
    public abstract void OnLeftClick();
    /// <summary> 우클릭 </summary>
    public abstract void OnRightClick();
    /// <summary> 마우스가 슬롯에 있을 때 </summary>
    public abstract void OnPointerIn();
    /// <summary> 마우스가 슬롯밖으로 나올 때 </summary>
    public abstract void OnPointerOut();

    #region Pointer Event

    public void OnPointerClick(PointerEventData eventData)
    {
        if (_pointedSlot != null)
        {
            EventSystem.current.SetSelectedGameObject(_pointedSlot.gameObject);

            // 더블클릭 확인
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                OnLeftClick();
                if (Time.time - clickTime < 0.3f)
                {
                    OnDoubleClick();
                    OnPointerMove(eventData);
                    clickTime = -1;
                }
                else
                {
                    clickTime = Time.time;
                }
            }
            // 우클릭 확인
            else if (eventData.button == PointerEventData.InputButton.Right)
            {
                OnRightClick();
            }
        }

    }

    public void OnPointerMove(PointerEventData eventData)
    {
        _pointedSlot = RaycastAndGetFirstComponent<SlotBase>(eventData);

        if (_pointedSlot != null)
        {
            OnPointerIn();
        }
        else
        {
            OnPointerOut();
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        OnPointerOut();
    }

    #endregion
}