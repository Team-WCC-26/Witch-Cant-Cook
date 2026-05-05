using System.Collections.Generic;
using UnityEngine;

public class PlayerRagdollController
{
    private readonly PlayerBrain brain;
    private readonly PlayerAnimController animController;
    private readonly IReadOnlyList<BodyPart> bodyParts;

    private const float MaxEnterAngularVelocity = 3f;

    private bool isRagdoll;

    public PlayerRagdollController(PlayerBrain brain, PlayerAnimController animController)
    {
        this.brain = brain;
        this.animController = animController;
        bodyParts = brain.BodyParts;

        SetRagdollPhysicsActive(false);
    }

    public void Enter()
    {
        if (isRagdoll)
        {
            return;
        }

        isRagdoll = true;

        animController.ForceIdle();

        brain.Animator.enabled = false;
        brain.Col.isTrigger = true;

        if (brain.Rb != null)
        {
            brain.Rb.useGravity = false;
            brain.Rb.linearVelocity = Vector3.zero;
            brain.Rb.angularVelocity = Vector3.zero;
        }

        SetRagdollPhysicsActive(true);
        ClampRagdollAngularVelocity();

        Physics.SyncTransforms();
    }

    public void Recover()
    {
        if (!isRagdoll)
        {
            return;
        }

        isRagdoll = false;

        ResetRagdollVelocity();
        ResetRootVelocity();

        SetRagdollPhysicsActive(false);

        brain.Animator.enabled = true;
        brain.Col.isTrigger = false;

        if (brain.Rb != null)
        {
            brain.Rb.useGravity = true;
        }

        Physics.SyncTransforms();
    }

    private void SetRagdollPhysicsActive(bool active)
    {
        foreach (var part in bodyParts)
        {
            if (part.Rb == null)
            {
                continue;
            }

            part.Rb.isKinematic = !active;
            part.Rb.useGravity = active;
            part.Rb.detectCollisions = true;
        }
    }

    private void ClampRagdollAngularVelocity()
    {
        foreach (var part in bodyParts)
        {
            if (part.Rb == null)
            {
                continue;
            }

            part.Rb.angularVelocity = Vector3.ClampMagnitude(
                part.Rb.angularVelocity,
                MaxEnterAngularVelocity
            );
        }
    }

    private void ResetRagdollVelocity()
    {
        foreach (var part in bodyParts)
        {
            if (part.Rb == null)
            {
                continue;
            }

            part.Rb.linearVelocity = Vector3.zero;
            part.Rb.angularVelocity = Vector3.zero;
        }
    }

    private void ResetRootVelocity()
    {
        if (brain.Rb == null)
        {
            return;
        }

        brain.Rb.linearVelocity = Vector3.zero;
        brain.Rb.angularVelocity = Vector3.zero;
    }
}