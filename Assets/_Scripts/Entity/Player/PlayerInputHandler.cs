using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInputHandler : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private PlayerBrain brain;

    public Vector2 Move { get; private set; }
    public Vector2 LookDelta { get; private set; }

    #region
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
            Move = Vector2.zero;
            return;
        }
        Move = context.ReadValue<Vector2>();
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

    public void OnLeftClicked(InputAction.CallbackContext context)
    {
        if (!context.started) return;

        Debug.Log("Left Clicked");

        if (brain.Interact.IsHolding)
        {
            brain.Interact.Drop();
        }
        else
        {
            brain.Interact.TryPick();
        }
    }

    public void OnRightClicked(InputAction.CallbackContext context)
    {
        if (!context.started) return;

        Debug.Log("Right Clicked");

        if (!brain.Interact.IsHolding) return;
        brain.Interact.TryThrow();
    }
    #endregion
}