using Cysharp.Threading.Tasks;
using System.Threading;
using UnityEngine;
using UnityEngine.EventSystems;

public class AutoHideScrollbar : MonoBehaviour, IBeginDragHandler, IEndDragHandler
{
    [SerializeField] private GameObject _scrollbar;
    [SerializeField] private float _hideDelay = 2f;

    private float _lastInteractionTime;


    public void OnBeginDrag(PointerEventData eventData)
    {
        _scrollbar.SetActive(true);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        _lastInteractionTime = Time.time;

        if (_scrollbar.activeSelf)
        {
            StartTimer().Forget();
        }
    }

    private async UniTaskVoid StartTimer()
    {
        while (Time.time - _lastInteractionTime < _hideDelay)
        {
            await UniTask.Yield();
        }

        _scrollbar.SetActive(false);
    }
}
