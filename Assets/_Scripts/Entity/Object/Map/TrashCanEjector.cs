using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class TrashCanEjector : ObjEjector
{
    #region Fields

    private readonly TrashCanInteraction trashCan;
    private readonly Settings settings = new();
    private float nextEjectTime;

    #endregion

    #region Public API

    public TrashCanEjector(TrashCanInteraction trashCan) : base(trashCan)
    {
        this.trashCan = trashCan;
    }

    public void Enqueue(List<long> ids, List<string> playerIds, int seed, long delayMs)
    {
        Enqueue(
            new TrashRequest
            {
                ids = new List<long>(ids),
                playerIds = new List<string>(playerIds),
                seed = seed,
                // include Queue Delay
                readyAt = Time.time + Mathf.Max(0f, delayMs / 1000f)
            }
        );
    }

    public override void Reset()
    {
        base.Reset();
        nextEjectTime = 0f;
    }

    #endregion

    #region Ejection

    protected override Settings CurrentSettings
    {
        get
        {
            trashCan.ConfigureEjection(settings);
            return settings;
        }
    }

    protected override IEnumerator WaitForTargets(Request request)
    {
        TrashRequest trashRequest = (TrashRequest)request;
        float remainingDelay = Mathf.Max(trashRequest.readyAt, nextEjectTime) - Time.time;
        if (remainingDelay > 0f)
            yield return new WaitForSeconds(remainingDelay);

        List<long> ids = trashRequest.ids;
        float deadline = Time.unscaledTime + trashCan.ResponseTimeout;

        // Pending spawns and content removal
        while (Time.unscaledTime < deadline)
        {
            bool ready = true;
            foreach (long id in ids)
                ready &= trashCan.PrepareReturn(id);
            foreach (string playerId in trashRequest.playerIds)
                ready &= trashCan.FindPlayer(playerId) != null;

            if (ready) yield break;
            yield return null;
        }
    }

    protected override IEnumerable<Object> GetTargets(Request request)
    {
        TrashRequest trashRequest = (TrashRequest)request;
        foreach (string playerId in trashRequest.playerIds)
        {
            PlayerBrain player = trashCan.FindPlayer(playerId);
            if (player == null) trashCan.SkipPlayerReturn(playerId);
            yield return player;
        }

        foreach (long id in trashRequest.ids)
        {
            if (trashCan.CanEject(id))
                yield return trashCan.FindObj(id);
            else
            {
                trashCan.SkipReturn(id);
                yield return null;
            }
        }
    }

    protected override Vector3 GetBaseDirection(Object target)
    {
        return trashCan.GetEjectDirection(target as CatchableObj);
    }

    protected override void EjectTarget(Object target, Vector3 direction)
    {
        if (target is PlayerBrain player)
        {
            trashCan.ReleasePlayer(player);
            EjectPlayer(player, trashCan.PlayerEjectForce, direction);
        }
        else if (target is CatchableObj obj)
        {
            float force = trashCan.GetEjectForce(obj);
            trashCan.ReleaseObj(obj);
            EjectObj(obj, direction, force);
        }
        nextEjectTime = Time.time + CurrentSettings.interval;
    }

    #endregion

    #region Types

    private sealed class TrashRequest : Request
    {
        public List<long> ids;
        public List<string> playerIds;
        public float readyAt;
    }

    #endregion
}