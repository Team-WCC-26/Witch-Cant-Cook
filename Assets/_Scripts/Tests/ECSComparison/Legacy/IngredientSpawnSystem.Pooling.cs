using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Profiling;
using Unity.Transforms;
using UnityEngine;

// Only opt-in Worlds use these components. Existing game Worlds keep the original lifecycle.
public struct IngredientPooledActive : IComponentData, IEnableableComponent { }

[InternalBufferCapacity(0)]
public struct IngredientSpawnBatchElement : IBufferElementData
{
    public IngredientSpawnRequest Request;
}

public partial class IngredientSpawnSystem
{
    private static readonly ProfilerMarker BatchMarker = new("Ingredient.SpawnBatch.Process");
    private static readonly ProfilerMarker PooledCreateMarker = new("Ingredient.EntityPool.Create");
    private static readonly ProfilerMarker PooledActivateMarker = new("Ingredient.EntityPool.Activate");
    private static readonly ProfilerMarker PooledReturnMarker = new("Ingredient.EntityPool.Return");
    private Dictionary<GameObject, Entity> pairedEntities;
    private Dictionary<long, Entity> activePairedEntities;
    private EntityArchetype pairedArchetype;
    private Entity batchQueue;
    private EntityQuery pooledLegacyRequestQuery;
    public bool PairedPoolingEnabled { get; private set; }
    public int PairedEntityCapacity => pairedEntities?.Count ?? 0;
    public int PairedEntitiesCreated { get; private set; }
    public int PairedEntitiesReused { get; private set; }
    public int PendingBatchRequests => PairedPoolingEnabled && EntityManager.Exists(batchQueue)
        ? EntityManager.GetBuffer<IngredientSpawnBatchElement>(batchQueue).Length : 0;

    /// <summary>Opt-in before the first spawn. Tests enable this on their own World only.</summary>
    public void EnablePairedPooling(int capacity)
    {
        if (PairedPoolingEnabled) return;
        pairedEntities = new Dictionary<GameObject, Entity>(capacity);
        activePairedEntities = new Dictionary<long, Entity>(capacity);
        pairedArchetype = EntityManager.CreateArchetype(typeof(IngredientInfo), typeof(Health),
            typeof(IngredientPhysics), typeof(IngredientCombat), typeof(LocalTransform),
            typeof(NetworkID), typeof(NetworkRemoteSync), typeof(IngredientPooledActive));
        batchQueue = EntityManager.CreateEntity();
        EntityManager.AddBuffer<IngredientSpawnBatchElement>(batchQueue).EnsureCapacity(capacity);
        pooledLegacyRequestQuery = GetEntityQuery(ComponentType.ReadOnly<IngredientSpawnRequest>());
        PairedPoolingEnabled = true;
    }

    public void EnqueueBatch(IngredientSpawnRequest[] requests, int count)
    {
        if (!PairedPoolingEnabled) throw new InvalidOperationException("Paired pooling is not enabled.");
        var buffer = EntityManager.GetBuffer<IngredientSpawnBatchElement>(batchQueue);
        // Reserve once per batch, never allocate an Entity for each request.
        buffer.EnsureCapacity(buffer.Length + count);
        for (int i = 0; i < count; i++) buffer.Add(new IngredientSpawnBatchElement { Request = requests[i] });
    }

    public void CancelBatchRequests() { if (PairedPoolingEnabled) EntityManager.GetBuffer<IngredientSpawnBatchElement>(batchQueue).Clear(); }

    private void ProcessPooledRequests(EntityCommandBuffer ecb)
    {
        if (PendingBatchRequests == 0 && pooledLegacyRequestQuery.IsEmptyIgnoreFilter) return;
        using var scope = BatchMarker.Auto();
        // Entity creation can invalidate DynamicBuffer handles. Copy before processing, then clear.
        var buffer = EntityManager.GetBuffer<IngredientSpawnBatchElement>(batchQueue);
        using var batch = buffer.ToNativeArray(Allocator.Temp);
        buffer.Clear();
        for (int i = 0; i < batch.Length; i++) ProcessSpawn(batch[i].Request, ecb);
        // Keep compatibility with existing per-request producers. Snapshot before structural changes.
        using var legacy = pooledLegacyRequestQuery.ToComponentDataArray<IngredientSpawnRequest>(Allocator.Temp);
        using var entities = pooledLegacyRequestQuery.ToEntityArray(Allocator.Temp);
        for (int i = 0; i < legacy.Length; i++)
        {
            ProcessSpawn(legacy[i], ecb);
            ecb.DestroyEntity(entities[i]);
        }
    }

    public void PreparePooledObject(GameObject go)
    {
        if (!PairedPoolingEnabled || go == null || !go.TryGetComponent<CatchableObj>(out _) || pairedEntities.ContainsKey(go)) return;
        using var scope = PooledCreateMarker.Auto();
        Entity entity = EntityManager.CreateEntity(pairedArchetype);
        EntityManager.SetComponentData(entity, LocalTransform.Identity);
        EntityManager.SetComponentEnabled<IngredientPooledActive>(entity, false);
        pairedEntities.Add(go, entity);
        PairedEntitiesCreated++;
    }

    private void ActivatePooledEntity(GameObject go, Ingredient ingredient, long id, Vector3 position, Quaternion rotation)
    {
        bool reused = pairedEntities.TryGetValue(go, out Entity entity) && EntityManager.Exists(entity);
        if (!reused)
        {
            pairedEntities.Remove(go);
            PreparePooledObject(go);
            if (!pairedEntities.TryGetValue(go, out entity))
                throw new InvalidOperationException("A pooled ingredient requires CatchableObj.");
        }
        using var scope = PooledActivateMarker.Auto();
        var stat = DataManager.Instance.GetIngredientStat().Get(ingredient.statID);
        EntityManager.SetComponentData(entity, new IngredientInfo { ID = ingredient.id, Name = ingredient.name });
        EntityManager.SetComponentData(entity, new Health { Current = stat.hp, Max = stat.hp });
        EntityManager.SetComponentData(entity, new IngredientPhysics { Weight = stat.weight,
            Throwing = DataManager.ParseEnum<ThrowingType>(ingredient.throwing, ThrowingType.parabola) });
        EntityManager.SetComponentData(entity, new IngredientCombat { Damage = stat.damage, Tag = ingredient.tag });
        EntityManager.SetComponentData(entity, LocalTransform.FromPositionRotation(position, rotation));
        EntityManager.SetComponentData(entity, new NetworkID { Value = id });
        EntityManager.SetComponentData(entity, new NetworkRemoteSync { TargetPosition = position,
            TargetRotation = rotation, InterpolationSpeed = 15f });
        activePairedEntities.Add(id, entity);
        EntityManager.SetComponentEnabled<IngredientPooledActive>(entity, true);
        if (reused) PairedEntitiesReused++;
    }

    public bool TryReturnPooledEntity(long id)
    {
        if (!PairedPoolingEnabled || !activePairedEntities.Remove(id, out Entity entity)) return false;
        using var scope = PooledReturnMarker.Auto();
        if (!EntityManager.Exists(entity)) return true;
        EntityManager.SetComponentEnabled<IngredientPooledActive>(entity, false);
        EntityManager.SetComponentData(entity, default(NetworkID));
        EntityManager.SetComponentData(entity, default(IngredientInfo));
        EntityManager.SetComponentData(entity, default(Health));
        EntityManager.SetComponentData(entity, default(IngredientPhysics));
        EntityManager.SetComponentData(entity, default(IngredientCombat));
        EntityManager.SetComponentData(entity, default(NetworkRemoteSync));
        EntityManager.SetComponentData(entity, LocalTransform.Identity);
        return true;
    }

    public void ReleasePooledObject(GameObject go)
    {
        if (!PairedPoolingEnabled || !pairedEntities.Remove(go, out Entity entity) || !EntityManager.Exists(entity)) return;
        long id = EntityManager.GetComponentData<NetworkID>(entity).Value;
        if (id > 0) activePairedEntities.Remove(id);
        EntityManager.DestroyEntity(entity);
    }

    public bool TryGetPairedEntity(GameObject go, out Entity entity)
    {
        entity = Entity.Null;
        return PairedPoolingEnabled && pairedEntities.TryGetValue(go, out entity) && EntityManager.Exists(entity);
    }

    public void DisablePairedPooling()
    {
        if (!PairedPoolingEnabled) return;
        foreach (Entity entity in pairedEntities.Values)
            if (EntityManager.Exists(entity)) EntityManager.DestroyEntity(entity);
        if (EntityManager.Exists(batchQueue)) EntityManager.DestroyEntity(batchQueue);
        pairedEntities.Clear();
        activePairedEntities.Clear();
        PairedPoolingEnabled = false;
    }
}
