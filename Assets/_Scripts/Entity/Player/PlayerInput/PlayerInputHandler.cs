using System;
using UnityEngine;
using UnityEngine.InputSystem;

public enum KeyInput
{
    None = 0,
    Primary, //좌클릭
    Secondary, //우클릭
    Interact, //F
} 

public class PlayerInputHandler : MonoBehaviour
{
    //InputSystem의 KeyInput Event 발송
    public event Action<KeyInput> InputPerformed;

    public Vector2 RawMoveDir { get; private set; }
    public Vector2 RawLookDelta { get; private set; }
    public bool RawIsRunning { get; private set; } = false;

    #region Unity Callbacks
    private void LateUpdate()
    {
        RawLookDelta = Vector2.zero;
    }
    #endregion

    #region Input System Callbacks
    public void OnMove(InputAction.CallbackContext context)
    {
        if (context.canceled)
        {
            RawMoveDir = Vector2.zero;
            return;
        }
        RawMoveDir = context.ReadValue<Vector2>();
    }

    public void OnLook(InputAction.CallbackContext context)
    {
        if (context.canceled)
        {
            RawLookDelta = Vector2.zero;
            return;
        }
        RawLookDelta = context.ReadValue<Vector2>();
    }

    public void OnSprint(InputAction.CallbackContext context)
    {
        RawIsRunning = context.ReadValueAsButton();
    }

    //default : F키
    public void OnInteract(InputAction.CallbackContext context)
    {
        if (!context.started) return;
        InputPerformed?.Invoke(KeyInput.Interact);
    }

    //default: 마우스 좌클릭
    public void OnPrimaryTriggered(InputAction.CallbackContext context)
    {
        if (!context.started) return;
        InputPerformed?.Invoke(KeyInput.Primary);
    }

    //default: 마우스 우클릭
    public void OnSecondaryTriggered(InputAction.CallbackContext context)
    {
        if (!context.started) return;
        InputPerformed?.Invoke(KeyInput.Secondary);
    }
    #endregion
}