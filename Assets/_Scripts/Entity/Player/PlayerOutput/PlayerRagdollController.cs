using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerRagdollController
{
    private readonly PlayerBrain brain;
    private readonly IReadOnlyList<BodyPart> bodyParts;

    private const float PenetrationCheckPadding = 0.05f;
    private const float MaxResolveDistance = 0.8f;
    private const float TemporaryIgnoreDuration = 0.25f;

    private bool isRagdoll;

    public PlayerRagdollController(PlayerBrain brain)
    {
        this.brain = brain;
        bodyParts = brain.BodyParts;
    }

    public void Enter()
    {
        Enter(null);
    }

    public void Enter(Collider hitCollider)
    {
        if (isRagdoll)
        {
            return;
        }

        isRagdoll = true;

        brain.Animator.enabled = false;

        Physics.SyncTransforms();

        ResolveRagdollPenetration();
        ResetRagdollVelocity();

        if (hitCollider != null)
        {
            brain.StartCoroutine(TemporarilyIgnoreHitCollider(hitCollider));
        }

        brain.Col.enabled = false;
    }

    public void Recover()
    {
        if (!isRagdoll)
        {
            return;
        }

        isRagdoll = false;

        ResetRagdollVelocity();

        brain.Animator.enabled = true;
        brain.Col.enabled = true;

        Physics.SyncTransforms();
    }

    private IEnumerator TemporarilyIgnoreHitCollider(Collider hitCollider)
    {
        foreach (var part in bodyParts)
        {
            if (part.Col == null)
            {
                continue;
            }

            Physics.IgnoreCollision(part.Col, hitCollider, true);
        }

        yield return new WaitForSeconds(TemporaryIgnoreDuration);

        foreach (var part in bodyParts)
        {
            if (part.Col == null)
            {
                continue;
            }

            Physics.IgnoreCollision(part.Col, hitCollider, false);
        }
    }

    private void ResolveRagdollPenetration()
    {
        Vector3 totalOffset = Vector3.zero;

        foreach (var part in bodyParts)
        {
            Collider colA = part.Col;

            if (colA == null || !colA.enabled)
            {
                continue;
            }

            Bounds bounds = colA.bounds;
            float radius = bounds.extents.magnitude + PenetrationCheckPadding;

            Collider[] overlaps = Physics.OverlapSphere(
                bounds.center,
                radius,
                ~0,
                QueryTriggerInteraction.Ignore
            );

            foreach (var colB in overlaps)
            {
                if (colB == null || colB == colA)
                {
                    continue;
                }

                if (colB.transform.IsChildOf(brain.transform))
                {
                    continue;
                }

                bool isOverlapped = Physics.ComputePenetration(
                    colA,
                    colA.transform.position,
                    colA.transform.rotation,
                    colB,
                    colB.transform.position,
                    colB.transform.rotation,
                    out Vector3 direction,
                    out float distance
                );

                if (!isOverlapped)
                {
                    continue;
                }

                totalOffset += direction * distance;
            }
        }

        if (totalOffset.sqrMagnitude <= 0.0001f)
        {
            return;
        }

        totalOffset = Vector3.ClampMagnitude(totalOffset, MaxResolveDistance);
        brain.transform.position += totalOffset;

        Physics.SyncTransforms();
    }

    private void ResetRagdollVelocity()
    {
        foreach (var part in bodyParts)
        {
            part.Rb.linearVelocity = Vector3.zero;
            part.Rb.angularVelocity = Vector3.zero;
        }
    }
}