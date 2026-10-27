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

    private bool inputBlocked;
    public bool IsInputBlocked { get => inputBlocked || StageReturnFlow.IsInputBlocked; set => inputBlocked = value; }
    public bool IsMovementBlocked { get; set; }
    private Vector2 rawMoveDir;
    private Vector2 rawLookDelta;
    private bool rawIsRunning;
    private bool rawIsJumpHeld;
    public bool IsSecondaryHeld { get; private set; }
    public uint SecondaryReleaseVersion { get; private set; }
    public Vector2 RawMoveDir { get => (IsInputBlocked || IsMovementBlocked) ? Vector2.zero : rawMoveDir; private set => rawMoveDir = value; }
    public Vector2 RawLookDelta { get => IsInputBlocked ? Vector2.zero : rawLookDelta; private set => rawLookDelta = value; }
    public bool RawIsRunning { get => !IsInputBlocked && !IsMovementBlocked && rawIsRunning; private set => rawIsRunning = value; }
    public bool RawIsJumpHeld { get => !IsInputBlocked && !IsMovementBlocked && rawIsJumpHeld; private set => rawIsJumpHeld = value; }

    #region Unity Callbacks
    private void OnDisable()
    {
        RawIsJumpHeld = false;
        IsSecondaryHeld = false;
    }

    private void LateUpdate()
    {
        RawLookDelta = Vector2.zero;
    }
    #endregion

    public void ResetForStageReturn()
    {
        inputBlocked = false;
        IsMovementBlocked = false;
        rawMoveDir = Vector2.zero;
        rawLookDelta = Vector2.zero;
        rawIsRunning = false;
        rawIsJumpHeld = false;
        IsSecondaryHeld = false;
    }

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
        if (IsInputBlocked) return;
        if (!context.started) return;
        InputPerformed?.Invoke(KeyInput.Interact);
    }

    public void OnPrimaryTriggered(InputAction.CallbackContext context)
    {
        if (IsInputBlocked) return;
        if (!context.started) return;
        InputPerformed?.Invoke(KeyInput.Primary);
    }

    public void OnSecondaryTriggered(InputAction.CallbackContext context)
    {
        if (context.canceled)
        {
            IsSecondaryHeld = false;
            SecondaryReleaseVersion++;
            return;
        }
        if (IsInputBlocked) return;
        if (!context.started) return;
        IsSecondaryHeld = true;
        InputPerformed?.Invoke(KeyInput.Secondary);
    }

    #region Jump Input
    // Sends one-shot jump input from the Input System.
    public void OnJump(InputAction.CallbackContext context)
    {
        RawIsJumpHeld = context.ReadValueAsButton();
        if (IsInputBlocked || IsMovementBlocked) return;
        if (!context.started) return;
        InputPerformed?.Invoke(KeyInput.Jump);
    }
    #endregion
    #endregion
}
