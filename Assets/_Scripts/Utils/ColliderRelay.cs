using System;
using UnityEngine;

/// <summary>
/// 같은 GameObject의 Collider 입력을 선택한 모드의 이벤트로 전달한다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class ColliderRelay : MonoBehaviour
{
    [SerializeField] private bool useTrigger = true;
    [SerializeField] private LayerMask targetLayers = ~0;

    private Collider[] colliders;

    public event Action<Collider> TriggerEntered;
    public event Action<Collider> TriggerStayed;
    public event Action<Collider> TriggerExited;

    public event Action<Collision> CollisionEntered;
    public event Action<Collision> CollisionStayed;
    public event Action<Collision> CollisionExited;

    private void OnEnable() => ApplyMode();

    private void OnValidate() => ApplyMode();

    private void ApplyMode()
    {
        colliders = GetComponents<Collider>();
        foreach (Collider collider in colliders)
        {
            collider.isTrigger = useTrigger;
        }
    }

    private bool MatchesLayer(Collider other)
    {
        return other != null &&
            (targetLayers.value & (1 << other.gameObject.layer)) != 0;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (isActiveAndEnabled && useTrigger && MatchesLayer(other))
            TriggerEntered?.Invoke(other);
    }

    private void OnTriggerStay(Collider other)
    {
        if (isActiveAndEnabled && useTrigger && MatchesLayer(other))
            TriggerStayed?.Invoke(other);
    }

    private void OnTriggerExit(Collider other)
    {
        if (isActiveAndEnabled && useTrigger && MatchesLayer(other))
            TriggerExited?.Invoke(other);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (isActiveAndEnabled && !useTrigger && MatchesLayer(collision.collider))
            CollisionEntered?.Invoke(collision);
    }

    private void OnCollisionStay(Collision collision)
    {
        if (isActiveAndEnabled && !useTrigger && MatchesLayer(collision.collider))
            CollisionStayed?.Invoke(collision);
    }

    private void OnCollisionExit(Collision collision)
    {
        if (isActiveAndEnabled && !useTrigger && MatchesLayer(collision.collider))
            CollisionExited?.Invoke(collision);
    }
}
