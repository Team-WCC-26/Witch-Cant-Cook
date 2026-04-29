using System;
using UnityEngine;
using UnityEngine.InputSystem;

[Flags]
public enum KeyInput
{
    None = 0,
    Primary = 1 << 0, //좌클릭
    Secondary = 1 << 1, //우클릭
    Interact = 1 << 2, //E
} 

public class PlayerInputHandler : MonoBehaviour
{
    //InputSystem의 KeyInput Event 발송
    public event Action<KeyInput> InputPerformed;

    public Vector2 MoveDir { get; private set; }
    public Vector2 LookDelta { get; private set; }
    public bool IsRunning { get; private set; } = false;

    #region Unity Callbacks
    private void LateUpdate()
    {
        LookDelta = Vector2.zero;
    }
    #endregion

    #region Input System Callbacks
    public void OnMove(InputAction.CallbackContext context)
    {
        if (context.canceled)
        {
            MoveDir = Vector2.zero;
            return;
        }
        MoveDir = context.ReadValue<Vector2>();
    }

    public void OnLook(InputAction.CallbackContext context)
    {
        if (context.canceled)
        {
            LookDelta = Vector2.zero;
            return;
        }
        LookDelta = context.ReadValue<Vector2>();
    }

    //default: 마우스 좌클릭
    public void OnPrimaryTriggered(InputAction.CallbackContext context)
    {
        if (!context.started) return;

        Debug.Log("Left Clicked");
        InputPerformed?.Invoke(KeyInput.Primary);

        //if (brain.Interact.IsHolding)
        //{
        //    brain.Interact.Drop();
        //}
        //else
        //{
        //    brain.Interact.TryPick();
        //}
    }

    //default: 마우스 우클릭
    public void OnSecondaryTriggered(InputAction.CallbackContext context)
    {
        if (!context.started) return;

        Debug.Log("Right Clicked");
        InputPerformed?.Invoke(KeyInput.Secondary);

        //if (!brain.Interact.IsHolding) return;
        //brain.Interact.TryThrow();
    }
    #endregion
}