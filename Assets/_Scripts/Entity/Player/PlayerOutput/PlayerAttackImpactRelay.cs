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
        brain?.Interact?.OnPunchImpact();
    }

    public void OnKnifeImpact()
    {
        brain?.Interact?.OnKnifeImpact();
    }
}
