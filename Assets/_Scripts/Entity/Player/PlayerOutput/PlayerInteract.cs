using System;
using UnityEngine;

public class PlayerInteract
{
    private readonly PlayerBrain brain;

    private const float DefaultThrowForce = 8.0f;
    private const bool DebugInteraction = true;

    public CatchableObj HeldObj { get; private set; }
    public bool IsHolding => HeldObj != null;

    public PlayerInteract(PlayerBrain brain)
    {
        this.brain = brain;
    }

    public void Handle(PlayerInteraction interaction)
    {
        DrawDebugInteractRay();

        switch (interaction)
        {
            case PlayerInteraction.Pick:
                DebugLog("Primary clicked. Try pick.");
                TryPick();
                break;
            case PlayerInteraction.Drop:
                DebugLog("Primary clicked. Drop held object.");
                Drop();
                break;
            case PlayerInteraction.Throw:
                DebugLog("Secondary clicked. Throw held object.");
                Throw();
                break;
        }
    }

    private void TryPick()
    {
        if (IsHolding) return;
        if (brain.ItemHoldParent == null) return;

        CatchableObj obj = FindCatchable();
        if (obj == null)
        {
            DebugLog("No catchable object on interact ray.");
            return;
        }
        if (obj.IsHold) return;
        if (!obj.CanBePicked) return;

        HeldObj = obj;
        Pick();
    }

    private void Drop()
    {
        if (!IsHolding) return;

        CatchableObj target = HeldObj;
        HeldObj = null;

        target.transform.SetParent(null, true);
        target.OnDrop();
    }

    private void Throw()
    {
        if (!IsHolding) return;

        CatchableObj target = HeldObj;
        HeldObj = null;

        target.transform.SetParent(null, true);
        target.transform.position = brain.ItemHoldParent != null
            ? brain.ItemHoldParent.position
            : target.transform.position;
        target.OnThrow();

        Rigidbody targetRb = target.Rb;
        if (targetRb == null) return;

        Vector3 throwDir = GetAimDirection();
        float throwForce = target.ThrowForce > 0.0f ? target.ThrowForce : DefaultThrowForce;

        targetRb.linearVelocity = Vector3.zero;
        targetRb.angularVelocity = Vector3.zero;
        targetRb.AddForce(throwDir * throwForce, ForceMode.Impulse);
    }

    private void Pick()
    {
        HeldObj.OnPick();
        HeldObj.transform.SetParent(brain.ItemHoldParent, false);
        HeldObj.transform.localPosition = HeldObj.HoldLocalPosition;
        HeldObj.transform.localRotation = Quaternion.Euler(HeldObj.HoldLocalEulerAngles);
    }

    private Ray BuildInteractRay()
    {
        Transform origin = brain.PlayerCam != null
            ? brain.PlayerCam.transform
            : brain.transform;

        Vector3 start = origin.position + origin.forward * brain.InteractRayStartOffset;
        return new Ray(start, origin.forward);
    }

    private CatchableObj FindCatchable()
    {
        RaycastHit[] hits = Physics.RaycastAll(BuildInteractRay(), brain.InteractDistance);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        for (int i = 0; i < hits.Length; i++)
        {
            Collider hitCollider = hits[i].collider;
            if (hitCollider == null) continue;
            if (hitCollider.transform.IsChildOf(brain.transform)) continue;

            CatchableObj obj = hitCollider.GetComponentInParent<CatchableObj>();
            DebugLog(obj != null
                ? $"Hit catchable object: {obj.name}"
                : $"Hit non-catchable object: {hitCollider.name}");

            return obj;
        }

        return null;
    }

    private void DrawDebugInteractRay()
    {
        if (!DebugInteraction) return;

        Ray ray = BuildInteractRay();
        Debug.DrawLine(ray.origin, ray.origin + ray.direction * brain.InteractDistance, Color.red);
    }

    private static void DebugLog(string message)
    {
        if (!DebugInteraction) return;

        Debug.Log($"[PlayerInteract] {message}");
    }

    private Vector3 GetAimDirection()
    {
        Transform origin = brain.PlayerCam != null
            ? brain.PlayerCam.transform
            : brain.transform;

        return origin.forward.normalized;
    }
}
