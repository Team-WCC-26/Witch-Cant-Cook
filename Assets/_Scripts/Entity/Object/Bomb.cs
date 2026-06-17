using MemoryPack;
using Protocol;
using Server;
using System;
using System.Collections;
using UnityEngine;

[RequireComponent(typeof(CatchableObj))]
public class Bomb : MonoBehaviour
{
    [Header("Fuse")]
    [SerializeField]
    private float fuseTime = 2f;

    [Header("Explosion")]
    [SerializeField]
    private float blastRadius = 4f;

    [SerializeField]
    private float blastForce = 12f;

    [SerializeField]
    private float upwardModifier = 2f;

    [SerializeField]
    private LayerMask blastMask;

    [Header("VFX / SFX")]
    [SerializeField]
    private ParticleSystem explosionVFX;

    [SerializeField]
    private AudioSource explosionSFX;

    private CatchableObj catchable;

    private Coroutine fuseCoroutine;

    private bool exploded;

    private void Awake()
    {
        catchable = GetComponent<CatchableObj>();
    }

    private void OnEnable()
    {
        exploded = false;
        GameEvents.OnEntityPicked += OnEntityPickup;
    }

    private void OnDisable()
    {
        StopFuse();
        GameEvents.OnEntityPicked -= OnEntityPickup;
    }

    private void OnEntityPickup(EntityPickedEvent evt)
    {
        if (evt.EntityId != catchable.NetworkId)
            return;

        StartFuse();
    }

    private void StartFuse()
    {
        if (fuseCoroutine != null)
            return;

        fuseCoroutine = StartCoroutine(FuseRoutine());
    }

    private void StopFuse()
    {
        if (fuseCoroutine == null)
            return;

        StopCoroutine(fuseCoroutine);
        fuseCoroutine = null;
    }

    private IEnumerator FuseRoutine()
    {
        yield return new WaitForSeconds(fuseTime);

        Explode();
    }

    private void Explode()
    {
        if (exploded)
            return;

        exploded = true;

        StopFuse();

        Vector3 origin = transform.position;

        //PlayVFX(origin);
        //PlaySFX();

        ApplyBlast(origin);

        ReturnToPool();
    }

    private void ApplyBlast(Vector3 origin)
    {
        Collider[] hits = Physics.OverlapSphere(
            origin,
            blastRadius,
            blastMask,
            QueryTriggerInteraction.Ignore
        );

        foreach (Collider hit in hits)
        {
            if (hit.gameObject == gameObject)
                continue;

            Rigidbody rb =
                        hit.attachedRigidbody != null
                            ? hit.attachedRigidbody
                            : hit.GetComponentInParent<Rigidbody>();
            if (rb == null)  continue;

            if (hit.TryGetComponent(out CatchableObj catchable))
            {
                if (catchable.IsHold)
                {
                    catchable.SetPhysicsState(true);
                }
            }
            Vector3 dir = rb.position - origin;
            dir.y *= 0.3f;
            Vector3 backDir = -transform.forward;

            Vector3 finalDir = (dir + backDir).normalized;
            Vector3 force =
            finalDir * blastForce +
            Vector3.up * (blastForce * 0.4f);

            rb.AddForce(force, ForceMode.Impulse);
            //    if (hit.TryGetComponent<PlayerBrain>(out var player))
            //    {
            //        rb = player.Rb;
            //    }
            //    else if (hit.TryGetComponent<CatchableObj>(out var obj))
            //    {
            //        rb = obj.Rb;

            //        if (obj.IsHold)
            //        {
            //            obj.SetPhysicsState(true);
            //        }
            //    }

            //    if (rb == null)
            //        continue;

            //    rb.AddExplosionForce(
            //        blastForce,
            //        origin,
            //        blastRadius,
            //        upwardModifier,
            //        ForceMode.Impulse
            //    );
        }
    }

    private void PlayVFX(Vector3 position)
    {
        if (explosionVFX == null)
            return;

        explosionVFX.transform.SetParent(null);

        explosionVFX.transform.position = position;

        explosionVFX.Play();
    }

    private void PlaySFX()
    {
        if (explosionSFX == null)
            return;

        explosionSFX.transform.SetParent(null);

        explosionSFX.Play();
    }

    private void ReturnToPool()
    {
        ObjectPoolManager.Instance.Push(gameObject);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0f, 0f, 0.25f);
        Gizmos.DrawSphere(transform.position, blastRadius);
    }
#endif
}