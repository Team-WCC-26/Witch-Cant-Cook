using DG.Tweening;
using UnityEngine;

public sealed class OvenDoorController : MonoBehaviour
{
    [SerializeField] private Transform door;
    [SerializeField] private Collider doorCollider;
    [SerializeField, Min(0f)] private float closeDuration = 0.5f;
    [SerializeField, Min(0f)] private float openDuration = 0.5f;

    private Quaternion openRotation;
    private Quaternion closedRotation;
    private Tween doorTween;
    private bool isInitialized;

    public bool IsClosed { get; private set; }
    public bool IsMoving => doorTween != null && doorTween.IsActive();

    #region Unity Callbacks
    private void Awake()
    {
        InitDoor();
    }

    private void OnDisable()
    {
        doorTween?.Kill();
        doorTween = null;
        if (!isInitialized) return;

        door.localRotation = openRotation;
        MoveCollider(openRotation);
        IsClosed = false;
    }
    #endregion

    public void CloseDoor()
    {
        if (!InitDoor() || IsClosed) return;
        IsClosed = true;
        MoveDoor(closedRotation, closeDuration);
    }

    public void OpenDoor()
    {
        if (!InitDoor() || !IsClosed) return;
        IsClosed = false;
        MoveDoor(openRotation, openDuration);
    }

    private bool InitDoor()
    {
        if (isInitialized) return true;
        if (door == null || doorCollider == null || doorCollider.transform == door)
        {
            Debug.LogError("Oven door requires a model Transform and a separate DoorCollider.", this);
            return false;
        }

        //열림 각도
        openRotation = door.localRotation; 
        //닫힘 각도
        Vector3 closedEuler = door.localEulerAngles;
        closedEuler.x = -90f;
        closedRotation = Quaternion.Euler(closedEuler);

        MoveCollider(openRotation); //초기 회전값 동기화.
        
        isInitialized = true;
        return true;
    }

    private void MoveDoor(Quaternion rotation, float duration)
    {
        doorTween?.Kill();
        doorTween = null;
        MoveCollider(rotation);

        if (duration <= 0f)
        {
            door.localRotation = rotation;
            return;
        }

        doorTween = door.DOLocalRotateQuaternion(rotation, duration)
            .SetEase(Ease.InOutSine)
            .OnComplete(() => doorTween = null);
    }

    private void MoveCollider(Quaternion rotation)
    {
        doorCollider.enabled = false;
        doorCollider.transform.localRotation = rotation;
        doorCollider.enabled = true;
    }
}