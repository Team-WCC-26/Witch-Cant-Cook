using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
public class ButtonSpriteHandler : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler, IPointerExitHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public Action<ButtonSpriteHandler> OnEnter;

    [SerializeField] private bool _isResetOnRealease = true;
    [SerializeField] private bool _isResetOnExit = true;
    [SerializeField] private bool _isSetSizeToSprite = true;
    [SerializeField] private ButtonSpriteHandler _innerSpriteHandler;

    [Header("Sprites")]
    [SerializeField] private Sprite _defaultSprite;
    [SerializeField] private Sprite _pressedSprite;
    [SerializeField] private Sprite _disabledSprite;
    [SerializeField] private Sprite _enterSprite;

    private Image _image;
    private Button _button;
    private RectTransform _rect;

    private bool _isFixed = false;
    private bool _isPressed = false;

    private void Awake()
    {
        Init();
    }

    public void Init()
    {
        if (_image == null)
        {
            _image = GetComponent<Image>();
        }

        _image.sprite = _defaultSprite;

        if (_button == null)
        {
            _button = GetComponent<Button>();
        }

        if (_rect == null)
        {
            _rect = GetComponent<RectTransform>();
        }

        Sprite defaultSprite = GetComponent<Sprite>();

        if (_defaultSprite == null)
        {
            _defaultSprite = defaultSprite;
        }
    }

    public void SetIsFixed(bool isFixed)
    {
        _isFixed = isFixed;
    }

    public void SetNormalSprite()
    {
        _isFixed = false;

        if (_defaultSprite == null) return;

        if (_button && !_button.interactable) return;

        if (_image == null)
        {
            _image = GetComponent<Image>();
        }

        _image.sprite = _defaultSprite;
        ChangeSize(_defaultSprite);

        _innerSpriteHandler?.SetNormalSprite();
    }

    public void SetPressedSprite()
    {
        if (_pressedSprite == null) return;

        if (_button && !_button.interactable) return;

        if (_image == null)
        {
            _image = GetComponent<Image>();
        }

        _image.sprite = _pressedSprite;
        ChangeSize(_pressedSprite);

        _innerSpriteHandler?.SetPressedSprite();
    }

    public void SetDisabledSprite()
    {
        if (_disabledSprite == null) return;

        if (_button && !_button.interactable) return;

        if (_image == null)
        {
            _image = GetComponent<Image>();
        }

        _image.sprite = _disabledSprite;
        ChangeSize(_disabledSprite);

        _innerSpriteHandler?.SetDisabledSprite();
    }

    public void SetEnterSprite()
    {
        if (_enterSprite == null) return;

        if (_button && !_button.interactable) return;

        if (_image == null)
        {
            _image = GetComponent<Image>();
        }

        _image.sprite = _enterSprite;
        ChangeSize(_enterSprite);

        _innerSpriteHandler?.SetEnterSprite();
    }

    private void ChangeSize(Sprite sprite)
    {
        if (!_isSetSizeToSprite) return;

        _rect.sizeDelta = sprite.bounds.size * 100;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        _isPressed = true;

        if (_isFixed) return;

        SetPressedSprite();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        _isPressed = false;

        if (_isFixed) return;

        if (_isResetOnRealease ||
            eventData.pointerCurrentRaycast.gameObject != gameObject &&
            _isResetOnExit)
        {
            SetNormalSprite();
        }
        else
        {
            _isFixed = true;
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        SetEnterSprite();
        OnEnter?.Invoke(this);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (_isFixed || _isPressed || !_isResetOnExit) return;

        SetNormalSprite();
    }

    #region 이벤트 가로챔 방지

    public void OnBeginDrag(PointerEventData eventData) => eventData.Use();
    public void OnDrag(PointerEventData eventData) => eventData.Use();
    public void OnEndDrag(PointerEventData eventData) => eventData.Use();

    #endregion
}