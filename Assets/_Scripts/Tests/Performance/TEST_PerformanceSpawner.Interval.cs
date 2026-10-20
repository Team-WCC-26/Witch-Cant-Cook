using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Profiling;
using UnityEngine;

public sealed partial class TEST_PerformanceSpawner
{
    private static readonly ProfilerMarker IntervalReturnMarker = new("TEST.Interval.ReturnToPool");
    public struct IntervalSpawnEvent
    {
        public long NetworkId;
        public int Repeat, Burst, InstanceId, SubmittedFrame, ActivatedFrame, ReturnedFrame;
        public double ScheduledAt, SubmittedAt, ActivatedAt, ReturnedAt;
        public bool ReusedAfterReturn, Cancelled;
    }
    private struct IntervalSlot
    {
        public IngredientSpawnRequest Request;
        public int EventIndex, ItemIndex;
        public double ReturnAt;
    }
    private IntervalSlot[] intervalSlots;
    private IntervalSpawnEvent[] intervalEvents;
    private Stack<int> intervalFree;
    private List<int> intervalPending, intervalActive;
    private HashSet<int> intervalSeenInstances;
    private double intervalLifetime;
    public bool IntervalSessionActive { get; private set; }
    public int IntervalPendingCount => intervalPending?.Count ?? 0;
    public int IntervalActiveCount => intervalActive?.Count ?? 0;
    public int IntervalSubmittedTotal { get; private set; }
    public int IntervalActivatedTotal { get; private set; }
    public int IntervalReturnedTotal { get; private set; }
    public int IntervalReusedTotal { get; private set; }
    public int IntervalEventCount { get; private set; }
    public int IntervalEntityCount => useEcs && queriesCreated && world != null && world.IsCreated ? ingredientQuery.CalculateEntityCount() : 0;
    public bool IntervalCleanupComplete => !useEcs || (queriesCreated && world != null && world.IsCreated && destroyQuery.IsEmptyIgnoreFilter);
    public IntervalSpawnEvent GetIntervalEvent(int index) => intervalEvents[index];

    public void PrepareIntervalBuffers(int liveCapacity, int eventCapacity, int prewarmedCount)
    {
        if (!CanSpawn || !TryGetIngredient(out _)) throw new InvalidOperationException(Status);
        EnsureQueries();
        intervalSlots = new IntervalSlot[liveCapacity];
        intervalEvents = new IntervalSpawnEvent[eventCapacity];
        intervalFree = new Stack<int>(liveCapacity);
        intervalPending = new List<int>(liveCapacity);
        intervalActive = new List<int>(liveCapacity);
        intervalSeenInstances = new HashSet<int>(Math.Max(liveCapacity, prewarmedCount));
        items.Capacity = Math.Max(items.Capacity, liveCapacity);
        PrepareItemCache(liveCapacity);
        if (comparisonRequests == null || comparisonRequests.Length < liveCapacity) Array.Resize(ref comparisonRequests, liveCapacity);
        IntervalEventCount = 0;
        ResetIntervalCounters();
    }

    public void ResetIntervalCounters()
    {
        if (IntervalSessionActive) throw new InvalidOperationException("Finish the interval run before resetting counters.");
        IntervalSubmittedTotal = IntervalActivatedTotal = IntervalReturnedTotal = IntervalReusedTotal = 0;
        MemoryStage = MemoryTestStage.Ready;
    }

    public void StartIntervalRepetition(double lifetimeSeconds)
    {
        if (!CanSpawn || intervalSlots == null || !IntervalCleanupComplete)
            throw new InvalidOperationException("Interval buffers or cleanup are not ready.");
        intervalLifetime = lifetimeSeconds;
        intervalPending.Clear(); intervalActive.Clear(); intervalFree.Clear(); intervalSeenInstances.Clear();
        items.Clear();
        for (int i = intervalSlots.Length - 1; i >= 0; i--) intervalFree.Push(i);
        IntervalSessionActive = true;
        MemoryStage = MemoryTestStage.Spawned;
        Status = "Interval test running";
    }

    public void SubmitIntervalBurst(int count, int burst, int repeat, double scheduledAt, double now)
    {
        if (!IntervalSessionActive || intervalFree.Count < count || IntervalEventCount > intervalEvents.Length - count)
            throw new InvalidOperationException("Interval tracking capacity exceeded. Check interval/lifetime or pending spawn backlog.");
        if (nextNetworkId > long.MaxValue - count) throw new InvalidOperationException("Test NetworkID range exhausted.");
        // Placement and bookkeeping are outside the measured request submission marker.
        for (int i = 0; i < count; i++)
        {
            long id = nextNetworkId++;
            if (ObjectPoolManager.Instance.activeObjDict.ContainsKey(id) || bootstrap.Router.catchableDics.ContainsKey(id) || ConveyorBeltRegistry.IsOwnedByAnyBelt(id))
                throw new InvalidOperationException("Interval NetworkID collision.");
            int slot = intervalFree.Pop();
            IngredientSpawnRequest data = CreateIntervalSpawnData(slot, i, IntervalSubmittedTotal + i, id);
            int record = IntervalEventCount++;
            intervalEvents[record] = new IntervalSpawnEvent { NetworkId = id, Repeat = repeat, Burst = burst,
                ScheduledAt = scheduledAt, SubmittedAt = now, SubmittedFrame = Time.frameCount };
            intervalSlots[slot] = new IntervalSlot { Request = data, EventIndex = record, ItemIndex = -1 };
            intervalPending.Add(slot);
            comparisonRequests[i] = data;
        }
        using (ComparisonSubmitMarker.Auto())
        {
            for (int i = 0; i < count; i++)
            {
                IngredientSpawnRequest data = comparisonRequests[i];
                if (useCentralized) centralSpawner.Enqueue(new IngredientSpawnCommand { IngredientID = data.IngredientID,
                    NetworkID = data.NetworkID, Position = data.Position, Rotation = data.Rotation, ConveyId = data.ConveyId });
                else if (useEcs && pairedSpawnSystem == null)
                {
                    Entity request = world.EntityManager.CreateEntity(typeof(IngredientSpawnRequest));
                    world.EntityManager.SetComponentData(request, data);
                }
            }
            pairedSpawnSystem?.EnqueueBatch(comparisonRequests, count);
        }
        IntervalSubmittedTotal += count;
    }

    private IngredientSpawnRequest CreateIntervalSpawnData(int slot, int inBurst, int ordinal, long id)
    {
        if (!useConveyor && ConveyorBeltRegistry.TryGetBeltById(0, out _))
            throw new InvalidOperationException("Belt ID 0 is reserved for floor requests.");
        Vector3 position = startPosition + (useGrid
            ? new Vector3(slot % gridColumns * spacing.x, 0f, slot / gridColumns * spacing.z) : spacing * slot);
        Quaternion rotation = Quaternion.identity;
        ConveyorBeltController belt = null;
        if (useConveyor)
        {
            if (conveyorBelts == null || conveyorBelts.Length == 0) throw new InvalidOperationException("Assign conveyor lanes.");
            int lane = ordinal % conveyorBelts.Length;
            if (!ValidateBeltSlot(lane + inBurst / conveyorBelts.Length * conveyorBelts.Length))
                throw new InvalidOperationException(Status);
            belt = conveyorBelts[lane];
            // Every burst enters near the start, like periodic server spawn packets.
            var sample = belt.Path.Evaluate(conveyorStartDistance + inBurst / conveyorBelts.Length * conveyorSpacing);
            position = sample.point;
            if (sample.tangent.sqrMagnitude > .0001f) rotation = Quaternion.LookRotation(sample.tangent, Vector3.up);
        }
        return new IngredientSpawnRequest { IngredientID = ingredientId, NetworkID = id,
            Position = position, Rotation = rotation, ConveyId = belt != null ? belt.BeltId : 0 };
    }

    private void VerifyIntervalSpawns()
    {
        if (MemoryStage == MemoryTestStage.Failed) return;
        try
        {
            double now = Time.realtimeSinceStartupAsDouble;
            // Historical GO backend retains its existing pool initialization sequence.
            if (!useEcs && !useCentralized && intervalPending.Count > 0)
                using (GameObjectSpawnMarker.Auto())
                    for (int i = 0; i < intervalPending.Count; i++)
                    {
                        var request = intervalSlots[intervalPending[i]].Request;
                        if (!ObjectPoolManager.Instance.activeObjDict.ContainsKey(request.NetworkID) && !SpawnGameObjectRecord(request)) return;
                    }
            now = Time.realtimeSinceStartupAsDouble;
            using var scope = VerifyMarker.Auto();
            for (int i = intervalPending.Count - 1; i >= 0; i--)
            {
                int index = intervalPending[i];
                ref IntervalSlot slot = ref intervalSlots[index];
                ref IntervalSpawnEvent record = ref intervalEvents[slot.EventIndex];
                if (!ObjectPoolManager.Instance.activeObjDict.TryGetValue(slot.Request.NetworkID, out UnityEngine.Object obj) || obj is not GameObject go)
                {
                    if (now - record.SubmittedAt > 15) throw new TimeoutException("An interval request was not consumed within 15 seconds.");
                    continue;
                }
                if (!go.activeInHierarchy || !bootstrap.Router.TryGet(record.NetworkId, out CatchableObj food) || food.gameObject != go || food.Data?.id != ingredientId)
                    throw new InvalidOperationException("Interval spawn identity/data/router mismatch.");
                ConveyorBeltController belt = null;
                ConveyorItemState beltState = null;
                if (useConveyor && (!ConveyorBeltRegistry.TryGetOwner(record.NetworkId, out belt) || !belt.TryGetItem(record.NetworkId, out beltState)))
                    throw new InvalidOperationException("Interval conveyor registration missing.");
                record.InstanceId = go.GetInstanceID();
                record.ActivatedAt = now; record.ActivatedFrame = Time.frameCount;
                record.ReusedAfterReturn = !intervalSeenInstances.Add(record.InstanceId);
                if (record.ReusedAfterReturn) IntervalReusedTotal++;
                slot.ReturnAt = now + intervalLifetime;
                slot.ItemIndex = items.Count;
                SpawnedItem item = itemCache[index];
                item.IntervalSlot = index; item.NetworkId = record.NetworkId; item.GameObject = go; item.Belt = belt; item.BeltState = beltState;
                items.Add(item); intervalActive.Add(index);
                intervalPending[i] = intervalPending[intervalPending.Count - 1]; intervalPending.RemoveAt(intervalPending.Count - 1);
                IntervalActivatedTotal++;
            }
            VerifiedObjectCount = IntervalActiveCount;
            VerifiedEntityCount = IntervalEntityCount;
        }
        catch (Exception exception) { Fail(exception.Message); }
    }

    public void ReturnDueIntervalItems(double now)
    {
        if (!IntervalSessionActive) return;
        bool scopeStarted = false;
        try
        {
            for (int i = intervalActive.Count - 1; i >= 0; i--)
            {
                int index = intervalActive[i];
                ref IntervalSlot slot = ref intervalSlots[index];
                if (now < slot.ReturnAt) continue;
                if (!scopeStarted) { IntervalReturnMarker.Begin(); scopeStarted = true; }
                ref IntervalSpawnEvent record = ref intervalEvents[slot.EventIndex];
                SpawnedItem item = items[slot.ItemIndex];
                if (item.GameObject == null || !item.GameObject.activeSelf || item.GameObject.GetComponent<CatchableObj>().NetworkId != record.NetworkId)
                    throw new InvalidOperationException("An interval ingredient disappeared before its scheduled return.");
                ObjectPoolManager.Instance.Push(item.GameObject);
                if (item.GameObject.activeSelf || bootstrap.Router.TryGet(record.NetworkId, out _) || ObjectPoolManager.Instance.activeObjDict.ContainsKey(record.NetworkId) || ConveyorBeltRegistry.IsOwnedByAnyBelt(record.NetworkId))
                    throw new InvalidOperationException("Interval pool return retained active registration.");
                record.ReturnedAt = now; record.ReturnedFrame = Time.frameCount;
                RemoveIntervalItem(slot.ItemIndex);
                intervalActive[i] = intervalActive[intervalActive.Count - 1]; intervalActive.RemoveAt(intervalActive.Count - 1);
                intervalFree.Push(index); IntervalReturnedTotal++;
            }
        }
        finally { if (scopeStarted) IntervalReturnMarker.End(); }
        VerifiedObjectCount = IntervalActiveCount;
        VerifiedEntityCount = IntervalEntityCount;
    }

    private void RemoveIntervalItem(int index)
    {
        SpawnedItem removed = items[index];
        SpawnedItem last = items[items.Count - 1];
        items[index] = last;
        intervalSlots[last.IntervalSlot].ItemIndex = index;
        items.RemoveAt(items.Count - 1);
        removed.NetworkId = 0; removed.GameObject = null; removed.Belt = null; removed.BeltState = null;
    }

    public void StopIntervalTracking(bool cancelled)
    {
        if (!IntervalSessionActive) return;
        if (useCentralized) centralSpawner.CancelPending();
        pairedSpawnSystem?.CancelBatchRequests();
        if (useEcs && queriesCreated && world != null && world.IsCreated)
        {
            using var requests = requestQuery.ToEntityArray(Allocator.Temp);
            world.EntityManager.DestroyEntity(requests);
        }
        for (int i = 0; i < intervalSlots.Length; i++)
        {
            var slot = intervalSlots[i];
            if (slot.Request.NetworkID == 0 || slot.EventIndex >= IntervalEventCount) continue;
            ref IntervalSpawnEvent record = ref intervalEvents[slot.EventIndex];
            if (record.ReturnedFrame != 0) continue;
            record.Cancelled = cancelled;
            if (ObjectPoolManager.Instance != null && ObjectPoolManager.Instance.activeObjDict.TryGetValue(record.NetworkId, out UnityEngine.Object obj) && obj is GameObject go && go != null)
            {
                ObjectPoolManager.Instance.Push(go);
                record.ReturnedAt = Time.realtimeSinceStartupAsDouble; record.ReturnedFrame = Time.frameCount;
            }
        }
        foreach (SpawnedItem item in items)
        { item.NetworkId = 0; item.GameObject = null; item.Belt = null; item.BeltState = null; }
        items.Clear(); intervalActive.Clear(); intervalPending.Clear();
        IntervalSessionActive = false;
        awaitingCleanup = useEcs && queriesCreated && world != null && world.IsCreated && !destroyQuery.IsEmptyIgnoreFilter;
        VerifiedObjectCount = VerifiedEntityCount = 0;
        Status = cancelled ? "Interval capture stopped; ingredients returned" : "Interval repetition completed";
    }
}
