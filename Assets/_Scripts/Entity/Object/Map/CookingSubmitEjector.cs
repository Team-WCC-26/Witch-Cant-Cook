using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Uses the server's list order and seed, including slots whose entities are missing.
public sealed class CookingSubmitEjector : ObjEjector
{
    private sealed class SubmitRequest : Request
    {
        public List<long> ids;
        public float ejectAt;
    }

    private readonly SubmitTriggerZone station;
    private readonly Settings settings = new();
    private readonly Dictionary<CatchableObj, long> targetIds = new();
    protected override Settings CurrentSettings
    {
        get { station.ConfigureEjection(settings); return settings; }
    }

    public CookingSubmitEjector(SubmitTriggerZone station) : base(station)
    {
        this.station = station;
    }

    public void Enqueue(List<long> ids, int seed, long delayMs)
    {
        base.Enqueue(new SubmitRequest
        {
            ids = new List<long>(ids), seed = seed,
            ejectAt = Time.unscaledTime + Mathf.Max(0f, delayMs / 1000f)
        });
    }

    protected override void OnRequestStarted(Request request) => targetIds.Clear();

    protected override IEnumerator WaitForTargets(Request request)
    {
        var submission = (SubmitRequest)request;
        // Start suction immediately; the server's delay is measured independently of timeScale.
        float ejectAt = submission.ejectAt;
        float deadline = Mathf.Max(ejectAt, Time.unscaledTime + station.ResponseTimeout);
        while (true)
        {
            bool ready = true;
            foreach (long id in submission.ids)
                if (id > 0) ready &= station.PrepareReturn(id);
            if (Time.unscaledTime >= ejectAt && (ready || Time.unscaledTime >= deadline)) yield break;
            yield return null;
        }
    }

    protected override IEnumerable<Object> GetTargets(Request request)
    {
        foreach (long id in ((SubmitRequest)request).ids)
        {
            if (id > 0 && station.CanEject(id))
            {
                CatchableObj item = station.FindItem(id);
                targetIds[item] = id;
                yield return item;
            }
            else { station.SkipReturn(id); yield return null; }
        }
    }

    protected override float PlayEjectVisual() => station.EjectDuration;

    protected override void EjectTarget(Object target, Vector3 direction)
    {
        if (target is not CatchableObj item || !targetIds.TryGetValue(item, out long id)) return;
        // A later slot can be pooled/reused while an earlier target is animating.
        if (item.NetworkId == id && station.CanEject(id)) station.StartEjection(item, direction);
        else station.SkipReturn(id);
    }

    protected override void OnRequestCompleted() => targetIds.Clear();
}
