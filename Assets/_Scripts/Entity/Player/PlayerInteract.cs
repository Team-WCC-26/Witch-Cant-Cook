using UnityEngine;

public class PlayerInteract : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private PlayerBrain brain;

    [Header("Hand Pos")]
    [SerializeField] private Transform leftHandPos;
    [SerializeField] private Transform rightHandPos;

    [Header("Interact Settings")]
    [SerializeField] private float rayStartOffset = 0.3f;
    [SerializeField] private float interactDistance = 3.0f;
    [SerializeField] private LayerMask interactLayer;
    [SerializeField] private float defaultThrowForce = 8.0f;

    public CatchableObj HeldObj { get; private set; }
    public bool IsHolding => HeldObj != null;

    private void Update()
    {
        DrawDebugInteractRay();
    }

    #region public methods
    public void TryPick()
    {
        if (IsHolding) return;

        Ray ray = BuildInteractRay();
        if (!Physics.Raycast(ray, out RaycastHit hit, interactDistance, interactLayer))
            return;

        CatchableObj obj = hit.collider.GetComponent<CatchableObj>();
        if (obj == null) return;
        if (obj.IsHold) return;
        if (!obj.CanBePicked) return;

        HeldObj = obj;
        Pick();
    }

    public void Drop()
    {
        if (!IsHolding) return;

        CatchableObj target = HeldObj;
        HeldObj = null;

        target.OnDrop();
        target.transform.SetParent(null, true);

        IgnoreCollisionWithPlayer(target, false);
    }

    public void TryThrow()
    {
        if (!IsHolding) return;

        CatchableObj target = HeldObj;
        HeldObj = null;

        target.OnThrow();
        target.transform.SetParent(null, true);

        IgnoreCollisionWithPlayer(target, false);

        Rigidbody targetRb = target.Rb;
        if (targetRb == null) return;

        Vector3 throwDir = GetAimDirection();
        float throwForce = target.ThrowForce > 0.0f ? target.ThrowForce : defaultThrowForce;

        targetRb.linearVelocity = Vector3.zero;
        targetRb.angularVelocity = Vector3.zero;
        targetRb.AddForce(throwDir * throwForce, ForceMode.Impulse);
    }
    #endregion

    #region interact 보조
    private void Pick()
    {
        HeldObj.OnPick();
        
        IgnoreCollisionWithPlayer(HeldObj, true);

        HeldObj.transform.SetParent(leftHandPos, false);
        HeldObj.transform.localPosition = HeldObj.HoldLocalPosition;
        HeldObj.transform.localRotation = Quaternion.Euler(HeldObj.HoldLocalEulerAngles);
    }
    #endregion

    #region Ray
    private Ray BuildInteractRay()
    {
        Transform cam = brain.PlayerCam.transform;

        Vector3 start = cam.position + cam.forward * rayStartOffset;
        Vector3 direction = cam.forward;

        return new Ray(start, direction);
    }

    private void DrawDebugInteractRay()
    {
        Transform cam = brain.PlayerCam.transform;

        Vector3 start = cam.position + cam.forward * rayStartOffset;
        Vector3 direction = cam.forward;

        Vector3 end = start + direction * interactDistance;

        Debug.DrawLine(start, end, Color.red);
    }
    #endregion

    #region 기타
    private Vector3 GetAimDirection()
    {
        Transform origin = brain.PlayerCam.transform;
        return origin.forward.normalized;
    }

    private void IgnoreCollisionWithPlayer(CatchableObj target, bool ignore)
    {
        if (brain.Col == null) return;

        Collider[] targetCols = target.GetComponentsInChildren<Collider>();
        for (int i = 0; i < targetCols.Length; i++)
        {
            if (targetCols[i] == null) continue;
            Physics.IgnoreCollision(brain.Col, targetCols[i], ignore);
        }
    }
    #endregion
}