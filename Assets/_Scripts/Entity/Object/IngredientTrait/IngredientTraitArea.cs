using System;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class IngredientTraitArea : MonoBehaviour
{
    public event Action<PlayerBrain> PlayerEntered;
    public event Action<PlayerBrain> PlayerStayed;
    public event Action<PlayerBrain> PlayerExited;

    [SerializeField] private LayerMask playerLayer;

    private void Reset()
    {
        if (TryGetComponent(out Collider col))
        {
            col.isTrigger = true;
        }
    }

    private bool IsPlayer(Collider other, out PlayerBrain player)
    {
        player = null;

        if (((1 << other.gameObject.layer) & playerLayer) == 0)
            return false;

        return other.TryGetComponent(out player);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsPlayer(other, out PlayerBrain player))
            return;

        PlayerEntered?.Invoke(player);
    }

    private void OnTriggerStay(Collider other)
    {
        if (!IsPlayer(other, out PlayerBrain player))
            return;

        PlayerStayed?.Invoke(player);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsPlayer(other, out PlayerBrain player))
            return;

        PlayerExited?.Invoke(player);
    }
}