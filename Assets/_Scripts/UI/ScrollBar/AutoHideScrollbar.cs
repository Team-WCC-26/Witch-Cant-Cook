using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(ScrollRect))]
public class AutoHideScrollbar : MonoBehaviour, IBeginDragHandler, IEndDragHandler
{
    [SerializeField] private Image _scrollbar;
    [SerializeField] private float _hideDelay = 2f;

    [Header("Fade")]
    [SerializeField] private float _showDuration = 0.2f;
    [SerializeField] private float _hideDuration = 0.3f;
    
    private ScrollRect _scrollRect;

    private float _lastInteractionTime;
    private bool _isDragging = false;

    private void Start()
    {
        _scrollRect = GetComponent<ScrollRect>();
        _scrollbar.DOFade(0, 0);
    }

    private void OnEnable()
    {
        _scrollRect.onValueChanged.AddListener(OnValueChanged);
    }

    private void OnDisable()
    {
        _scrollRect.onValueChanged.RemoveListener(OnValueChanged);
    }

    private void Update()
    {
        if (_isDragging || _scrollbar.color.a == 0f) return;

        if (Time.time - _lastInteractionTime > _hideDelay)
        {
            Hide();
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        _isDragging = true;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        _lastInteractionTime = Time.time;
        _isDragging = false;
    }

    private void OnValueChanged(Vector2 vec = default)
    {
        _lastInteractionTime = Time.time;
        Show();
    }

    private void Show()
    {
        if (_scrollbar.color.a == 0f)
        {
            _scrollbar.DOFade(1f, _showDuration);
        }
    }

    private void Hide()
    {
        if (_scrollbar.color.a == 1f)
        {
            _scrollbar.DOFade(0f, _hideDuration);
        }
    }
}
