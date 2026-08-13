using Protocol;
using System;
using UnityEngine;

[RequireComponent(typeof(CatchableObj))]
public class IngredientTraitFragile : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CatchableObj catchable;

    [Header("Break")]
    [SerializeField] private float breakImpactThreshold = 2f; // 충격량이 이 값 이상이면 깨짐

    [Header("Effect")]
    [SerializeField] private ParticleSystem breakEffect;

    public event Action OnBroken;

    private bool isBroken;

    private void Reset()
    {
        catchable = GetComponent<CatchableObj>();
    }

    private void Awake()
    {
        if (catchable == null)
            catchable = GetComponent<CatchableObj>();
    }

    private void OnEnable()
    {
        isBroken = false;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (isBroken)
            return;

        if (collision.relativeVelocity.magnitude < breakImpactThreshold)
            return;

        Break();
    }

    private void Break()
    {
        isBroken = true;

        Vector3 spawnPos = transform.position;

        if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, 2f))
        {
            spawnPos = hit.point;
        }
        // 깨진 위치에 영역 생성해야함. 영역 생성은 통합특성 스크립트에서 호출하는데.. 
        OnBroken?.Invoke();
    }
}