using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class TrashCanEjector : ObjEjector
{
    private sealed class TrashRequest : Request
    {
        public List<long> ids;
    }

    private readonly TrashCanInteraction trashCan;
    private readonly Settings settings = new();

    protected override Settings CurrentSettings
    {
        get { trashCan.ConfigureEjection(settings); return settings; }
    }

    public TrashCanEjector(TrashCanInteraction trashCan) : base(trashCan)
    {
        this.trashCan = trashCan;
    }

    public void Enqueue(List<long> ids, int seed, long delayMs)
    {
        base.Enqueue(new TrashRequest
        {
            ids = new List<long>(ids), seed = seed, delay = Mathf.Max(0f, delayMs / 1000f)
        });
    }

    protected override void OnRequestStarted(Request request) { }

    protected override IEnumerator WaitForTargets(Request request)
    {
        List<long> ids = ((TrashRequest)request).ids;
        float deadline = Time.unscaledTime + trashCan.ResponseTimeout;
        while (Time.unscaledTime < deadline)
        {
            bool ready = true;
            foreach (long id in ids) ready &= trashCan.PrepareReturn(id);
            if (ready) yield break;
            yield return null;
        }
    }

    protected override IEnumerable<Object> GetTargets(Request request)
    {
        foreach (long id in ((TrashRequest)request).ids)
        {
            if (trashCan.CanEject(id)) yield return trashCan.FindObj(id);
            else { trashCan.SkipReturn(id); yield return null; }
        }
    }

    protected override Vector3 GetBaseDirection(Object target)
    {
        return trashCan.GetEjectDirection(target as CatchableObj);
    }

    protected override void EjectTarget(Object target, Vector3 direction)
    {
        if (target is not CatchableObj obj) return;
        float force = trashCan.GetEjectForce(obj);
        trashCan.ReleaseObj(obj);
        EjectObj(obj, direction, force);
    }

    protected override void OnRequestCompleted() { }
}
