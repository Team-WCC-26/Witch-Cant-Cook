using System;
using UnityEngine;
using UnityEngine.InputSystem;

public enum KeyInput
{
    None = 0,
    Primary, //Left click
    Secondary, //Right click
    Interact, //F
    Jump, //Space
}

public class PlayerInputHandler : MonoBehaviour
{
    //InputSystem key input event sender
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

    public void OnInteract(InputAction.CallbackContext context)
    {
        if (!context.started) return;
        InputPerformed?.Invoke(KeyInput.Interact);
    }

    public void OnPrimaryTriggered(InputAction.CallbackContext context)
    {
        if (!context.started) return;
        InputPerformed?.Invoke(KeyInput.Primary);
    }

    public void OnSecondaryTriggered(InputAction.CallbackContext context)
    {
        if (!context.started) return;
        InputPerformed?.Invoke(KeyInput.Secondary);
    }

    #region Jump Input
    // Sends one-shot jump input from the Input System.
    public void OnJump(InputAction.CallbackContext context)
    {
        if (!context.started) return;
        InputPerformed?.Invoke(KeyInput.Jump);
    }
    #endregion
    #endregion
}
