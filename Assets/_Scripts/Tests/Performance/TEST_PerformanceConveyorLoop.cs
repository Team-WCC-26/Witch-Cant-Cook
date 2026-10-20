using Unity.Profiling;
using UnityEngine;

/// <summary>Test-only transport back to the start. Preserves the original GameObject, NetworkID and ECS Entity.</summary>
public sealed class TEST_PerformanceConveyorLoop : MonoBehaviour
{
    private static readonly ProfilerMarker CheckMarker = new("TEST.Performance.ConveyorLoop.Check");
    private static readonly ProfilerMarker RecycleMarker = new("TEST.Performance.ConveyorLoop.ReRegister");
    [SerializeField] private TEST_PerformanceSpawner spawner;

    private void LateUpdate()
    {
        if (spawner == null || !spawner.UseConveyor || spawner.Items.Count == 0) return;
        using var checkScope = CheckMarker.Auto();
        for (int i = 0; i < spawner.Items.Count; i++)
        {
            var item = spawner.Items[i];
            ConveyorBeltController belt = item.Belt;
            ConveyorItemState state = item.BeltState;
            if (belt == null || state == null || !state.IsMovingOnBelt || item.GameObject == null ||
                !item.GameObject.activeInHierarchy || (state.Catchable != null && state.Catchable.IsHold)) continue;
            float distance = (Time.time - state.LocalSpawnTime) * belt.Speed;
            if (distance < belt.Path.TotalLength) continue;
            using (RecycleMarker.Auto())
            {
                // Preserve overshoot so the lane spacing does not drift at different frame rates.
                float wrappedDistance = Mathf.Repeat(distance, belt.Path.TotalLength);
                var sample = belt.Path.Evaluate(wrappedDistance);
                Vector3 right = Vector3.Cross(Vector3.up, sample.tangent).normalized;
                Quaternion rotation = sample.tangent.sqrMagnitude > 0.0001f
                    ? Quaternion.LookRotation(sample.tangent, Vector3.up) : item.GameObject.transform.rotation;
                belt.UnregisterItem(item.NetworkId);
                item.GameObject.transform.SetPositionAndRotation(sample.point + right * state.LateralOffset, rotation);
                if (item.GameObject.TryGetComponent(out Rigidbody rb))
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                belt.RegisterItemAtDistance(item.NetworkId, item.GameObject.transform, item.GameObject, wrappedDistance);
                belt.TryGetItem(item.NetworkId, out item.BeltState);
            }
        }
    }
}
