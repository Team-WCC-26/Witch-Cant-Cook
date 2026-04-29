using System.Collections.Generic;
using UnityEngine;

public class PlayerRagdollController
{
    private readonly PlayerBrain brain;
    private readonly IReadOnlyList<BodyPart> bodyParts;

    private BodyPart Hip => bodyParts[0];

    public PlayerRagdollController(PlayerBrain brain)
    {
        this.brain = brain;
        bodyParts = brain.BodyParts;
    }

    public void Enter()
    {
        brain.Animator.enabled = false;
        brain.Col.enabled = false;

        Hip.gameObject.SetActive(true);
    }

    public void Recover()
    {
        foreach (var part in bodyParts)
        {
            part.transform.position = part.AnimatedBody.position;
            part.transform.rotation = part.AnimatedBody.rotation;
        }

        Hip.gameObject.SetActive(false);

        brain.Animator.enabled = true;
        brain.Col.enabled = true;
    }
}