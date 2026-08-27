using MemoryPack;
using Protocol;
using Server;
using System;
using UnityEngine;

public class IngredientTraitTracker : MonoBehaviour
{
    [Header("Tracking")]
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private float searchRadius = 20f;
    [SerializeField] private LayerMask playerLayer;

    private Rigidbody rb;
    private PlayerBrain target;
    private bool isTracking;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    private void OnEnable()
    {
        //ServerManager.Instance.RegisterHandler(PacketId.추적할플레이어, StartTracking);

    }

    private void StartTracking(ReadOnlyMemory<byte> data)
    {
        //어쩌고Packet packet = MemoryPackSerializer.Deserialize<어쩌고Packet>(data.Span);

        //if (isTracking) { }
        // 중간에 추적 플레이어 바뀌는 경우 있나?

        if (target == null)
        {
            isTracking = false;
            return;
        }

        isTracking = true;
    }

    public void StopTracking()
    {
        isTracking = false;
        target = null;

        if (rb != null)
            rb.linearVelocity = Vector3.zero;
    }

    private void FixedUpdate()
    {
        if (!isTracking || target == null)
            return;

        Vector3 dir = target.transform.position - transform.position;
        dir.y = 0f;

        if (dir.sqrMagnitude < 0.01f)
        {
            rb.linearVelocity = Vector3.zero;
            return;
        }

        rb.linearVelocity = dir.normalized * moveSpeed;
    }
}
