using System;
using UnityEngine;

public class TabHandler : MonoBehaviour
{
    public Action<int> OnIndexChanged;

    private ObjectActiveHandler[] _buttons;
    private ButtonSpriteHandler[] _spriteHandlers;

    private int _currentIndex;

    private void OnEnable()
    {
        for (int i = 0; i < _buttons.Length; i++)
        {
            _buttons[i].OnClicked += CloseCurrentActivePanel;
        }
    }

    private void OnDisable()
    {
        for (int i = 0; i < _buttons.Length; i++)
        {
            _buttons[i].OnClicked -= CloseCurrentActivePanel;
        }
    }

    private void Awake()
    {
        int cnt = transform.childCount;

        _buttons = new ObjectActiveHandler[cnt];
        _spriteHandlers = new ButtonSpriteHandler[cnt];

        for (int i = 0; i < cnt; i++)
        {
            Transform child = transform.GetChild(i);

            ObjectActiveHandler button = child.GetComponent<ObjectActiveHandler>();

            if (!child.TryGetComponent<ButtonSpriteHandler>(out var spriteHandler))
            {
                spriteHandler = button.gameObject.AddComponent<ButtonSpriteHandler>();
                spriteHandler.Init();
            }

            _buttons[i] = button;
            _spriteHandlers[i] = spriteHandler;
        }
    }

    private void Start()
    {
        for (int i = 1; i < transform.childCount; i++)
        {
            _buttons[i].ClosePanelForce();
            _spriteHandlers[i].SetNormalSprite();
        }

        _currentIndex = 0;
        _buttons[0].OpenPanelForce();
        _spriteHandlers[0].SetPressedSprite();
        _spriteHandlers[0].SetIsFixed(true);
    }

    private void CloseCurrentActivePanel(ObjectActiveHandler objectHandler)
    {
        int newIdx = Array.IndexOf(_buttons, objectHandler);

        if (_currentIndex == newIdx) return;

        _buttons[_currentIndex].ClosePanelForce();
        _spriteHandlers[_currentIndex].SetNormalSprite();

        _currentIndex = newIdx;
        OnIndexChanged?.Invoke(_currentIndex);
    }
}