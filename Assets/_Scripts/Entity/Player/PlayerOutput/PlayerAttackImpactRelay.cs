using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public sealed class PlayerAttackImpactRelay : MonoBehaviour
{
    private PlayerBrain brain;

    private void Awake()
    {
        brain = GetComponentInParent<PlayerBrain>();
    }

public void OnPunchImpact()
    {
        if (brain?.Interact == null) return;
        if (brain.StateResolver is RemotePlayerStateResolver)
            brain.Interact.OnRemotePunchImpact();
        else
            brain.Interact.OnPunchImpact();
    }

    public void OnKnifeImpact()
    {
        brain?.Interact?.OnKnifeImpact();
    }
}
