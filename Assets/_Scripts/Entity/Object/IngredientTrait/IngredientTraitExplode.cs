using MemoryPack;
using Protocol;
using Server;
using System;
using System.Collections;
using Unity.Entities;
using UnityEngine;

[RequireComponent(typeof(CatchableObj))]
public class IngredientTraitExplode : IngredientTrait
{
    [Header("Fragments")]
    [SerializeField] private GameObject explosionVFXPrefab;
    [SerializeField] private GameObject explosionFragment;
    [SerializeField] private int fragmentCount = 10;
    [SerializeField] private float fragmentForce = 8f;

    [Header("Fuse")]
    [SerializeField] private float holdFuseTime = 3f;
    [SerializeField] private float groundFuseTime = 10f;

    [Header("Explosion")]
    [SerializeField] private float blastRadius = 4f;
    [SerializeField] private float blastForce = 12f;
    [SerializeField] private float upwardModifier = 2f;
    [SerializeField] private LayerMask blastMask;

    [Header("VFX / SFX")]
    [SerializeField] private ParticleSystem explosionVFX;
    [SerializeField] private AudioSource explosionSFX;

    private CatchableObj catchable;

    private Coroutine fuseCoroutine;

    private bool previousHoldState;
    private bool exploded;

    private void Awake()
    {
        catchable = GetComponent<CatchableObj>();
    }

    private void OnEnable()
    {
        exploded = false;

        previousHoldState = catchable.IsHold;

        if (previousHoldState)
            StartFuse(holdFuseTime);
        else
            StartFuse(groundFuseTime);
    }
    private void OnDisable()
    {
        StopFuse();
    }

    private void Update()
    {
        if (previousHoldState == catchable.IsHold)
            return;

        previousHoldState = catchable.IsHold;

        // 상태가 바뀐 경우에만 타이머를 교체
        if (catchable.IsHold)
            StartFuse(holdFuseTime);
        else
            StartFuse(groundFuseTime);
    }


    private void StartFuse(float time)
    {
        StopFuse();
        fuseCoroutine = StartCoroutine(FuseRoutine(time));
    }
    private void StopFuse()
    {
        if (fuseCoroutine == null)
            return;

        StopCoroutine(fuseCoroutine);
        fuseCoroutine = null;
    }

    private IEnumerator FuseRoutine(float time)
    {
        yield return new WaitForSeconds(time);
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
        SpawnExplosionParticle(origin);

        ApplyBlast(origin);

        SpawnFragments(origin);

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

            if (hit.TryGetComponent(out PlayerBrain player))
            {
                ApplyPlayerKnockback(player.Rb);
                continue;
            }
            if (hit.TryGetComponent(out CatchableObj catchable))
            {
                if (catchable.IsHold)
                    catchable.SetPhysicsState(true);

                Rigidbody rb = catchable.Rb ?? hit.attachedRigidbody;

                if (rb == null)
                    continue;

                ApplyObjectKnockback(rb, origin);
            }
        }
        
    }
    private void ApplyObjectKnockback(Rigidbody rb, Vector3 origin)
    {
        Vector3 dir = rb.position - origin;

        // 수평 위주 퍼짐
        dir.y = 0f;

        if (dir.sqrMagnitude < 0.001f)
            dir = UnityEngine.Random.insideUnitSphere;

        dir.Normalize();

        Vector3 force =
            dir * blastForce +
            Vector3.up * (blastForce * 0.3f);

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        rb.AddForce(force, ForceMode.Impulse);
    }
    private void ApplyPlayerKnockback(Rigidbody rb)
    {
        Vector3 backDir = -transform.forward;
        backDir.y = 0f;
        backDir.Normalize();

        // 뒤 + 아주 약한 위로 (포물선 시작용)
        Vector3 force =
            backDir * blastForce * 2.2f +
            Vector3.up * blastForce * 0.25f;

        // 기존 속도 초기화
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        rb.AddForce(force, ForceMode.Impulse);

        Vector3 vel = rb.linearVelocity;

        if (vel.y > 2f)   // 너무 높게 뜨는 것만 컷
            vel.y = 2f;

        rb.linearVelocity = vel;
    }

    private void SpawnExplosionParticle(Vector3 position)
    {
        if (explosionVFXPrefab == null)
            return;

        GameObject vfx = Instantiate(explosionVFXPrefab, position, Quaternion.identity);
    }
    private void SpawnFragments(Vector3 origin)
    {
        for (int i = 0; i < fragmentCount; i++)
        {
            // 팝콘도 오브젝트 풀링 적용해야됨 -> 테이블에 팝콘 생기면?
            //GameObject obj =
            //    ObjectPoolManager.Instance.Pop(explosionFragment);

            GameObject obj = Instantiate(explosionFragment, origin, Quaternion.identity);
            obj.transform.position = origin;
            obj.transform.rotation = UnityEngine.Random.rotation;

            Rigidbody rb = obj.GetComponent<Rigidbody>();

            if (rb == null)
                continue;

            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            Vector3 dir = UnityEngine.Random.onUnitSphere;

            dir.y = Mathf.Abs(dir.y) + 0.3f;

            rb.AddForce(dir.normalized * fragmentForce,
                ForceMode.Impulse);
        }
    }

    #region SFX & VFX
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
    #endregion

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