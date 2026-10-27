using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Interaction의 보관/조리 상태와 분리된 공통 배출 실행기.
public abstract class ObjEjector
{
    public sealed class Settings
    {
        public Transform ejectPoint;
        public float interval;
        public Vector3 baseDirection;
        public float randomRangeX;
        public float randomRangeYMin;
        public float randomRangeYMax;
        public float randomRangeZ;
        public float force;
        public float gizmoLength;
    }

    protected abstract class Request
    {
        public int seed;
        public float delay;
    }

    private readonly MonoBehaviour host;
    private readonly Queue<Request> requests = new();
    private Coroutine coroutine;
    private System.Random random;

    protected abstract Settings CurrentSettings { get; }
    protected Transform Origin => host.transform;
    protected Vector3 EjectPos => CurrentSettings.ejectPoint != null
        ? CurrentSettings.ejectPoint.position : Origin.position;

    public bool IsEjecting { get; private set; }

    protected ObjEjector(MonoBehaviour host)
    {
        this.host = host;
    }

    public virtual void Reset()
    {
        if (coroutine != null && host != null)
            host.StopCoroutine(coroutine);

        coroutine = null;
        requests.Clear();
        random = null;
        IsEjecting = false;
    }

    protected void Enqueue(Request request)
    {
        if (request == null || host == null || !host.isActiveAndEnabled) return;

        requests.Enqueue(request);
        if (IsEjecting) return;

        IsEjecting = true;
        coroutine = host.StartCoroutine(EjectRoutine());
    }

    protected void EjectObj(CatchableObj catchable, Vector3 direction, float? force = null)
    {
        if (catchable == null) return;

        Transform point = CurrentSettings.ejectPoint;
        catchable.transform.SetParent(null, false);
        catchable.transform.SetPositionAndRotation(EjectPos, point != null ? point.rotation : Origin.rotation);
        catchable.gameObject.SetActive(true);
        catchable.ChangePickState(true);
        catchable.SetPhysicsState(true);
        if (catchable.Rb == null) return;

        catchable.Rb.linearVelocity = Vector3.zero;
        catchable.Rb.angularVelocity = Vector3.zero;
        catchable.Rb.AddForce(direction * (force ?? CurrentSettings.force), ForceMode.Impulse);
    }

    protected void EjectPlayer(PlayerBrain player, float force, Vector3 direction)
    {
        // Owner physics; remote movement snapshots
        if (player == null || PlayerSpawnManager.Instance == null ||
            !PlayerSpawnManager.Instance.IsMine(player.PlayerId)) return;
        if (player.Rb == null || player.Rb.isKinematic) return;

        player.ActionController.Movement.BeginEjectionMovement();
        player.Rb.position = EjectPos;
        player.Rb.linearVelocity = Vector3.zero;
        player.Rb.angularVelocity = Vector3.zero;
        player.Rb.AddForce(direction * force, ForceMode.Impulse);
    }

    protected virtual void OnRequestStarted(Request request) { }
    protected abstract IEnumerable<Object> GetTargets(Request request);
    protected abstract void EjectTarget(Object target, Vector3 direction);
    protected virtual void OnRequestCompleted() { }

    protected virtual IEnumerator WaitBeforeSequence(Request request)
    {
        yield break;
    }

    protected virtual float PlayEjectVisual() => 0f;

    protected virtual IEnumerator WaitForTargets(Request request) => null;

    protected virtual Vector3 GetBaseDirection(Object target) => CurrentSettings.baseDirection;

    private IEnumerator EjectRoutine()
    {
        while (requests.Count > 0)
        {
            Request request = requests.Dequeue();
            random = new System.Random(request.seed);
            OnRequestStarted(request);
            if (request.delay > 0f)
                yield return new WaitForSeconds(request.delay);
            IEnumerator targetWait = WaitForTargets(request);
            if (targetWait != null)
                yield return targetWait;
            List<Object> targets = new(GetTargets(request));
            if (targets.Count > 0)
                yield return WaitBeforeSequence(request);

            HashSet<Object> ejectedTargets = new();
            for (int index = 0; index < targets.Count; index++)
            {
                float visualDuration = PlayEjectVisual();
                // 제거·중복 대상도 같은 난수를 소비해 다음 대상의 방향을 유지한다.
                Object target = targets[index];
                Vector3 direction = GetDirection(target);
                if (target != null && ejectedTargets.Add(target))
                    EjectTarget(target, direction);

                float delay = index + 1 < targets.Count
                    ? Mathf.Max(CurrentSettings.interval, visualDuration) : visualDuration;
                if (delay > 0f)
                    yield return new WaitForSeconds(delay);
                else
                    yield return null;
            }

            // 빈 요청도 StartCoroutine의 반환 전에 완료되지 않게 한다.
            if (targets.Count == 0)
                yield return null;
            OnRequestCompleted();
        }

        coroutine = null;
        random = null;
        IsEjecting = false;
    }

    private Vector3 GetDirection(Object target)
    {
        Settings settings = CurrentSettings;
        float minY = Mathf.Min(settings.randomRangeYMin, settings.randomRangeYMax);
        float maxY = Mathf.Max(settings.randomRangeYMin, settings.randomRangeYMax);
        Vector3 offset = new(
            NextRange(-settings.randomRangeX, settings.randomRangeX),
            NextRange(minY, maxY),
            NextRange(-settings.randomRangeZ, settings.randomRangeZ));
        return GetWorldDirection(GetBaseDirection(target) + offset);
    }

    private Vector3 GetWorldDirection(Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.0001f)
            direction = CurrentSettings.baseDirection.sqrMagnitude >= 0.0001f
                ? CurrentSettings.baseDirection : Vector3.up;
        return Origin.TransformDirection(direction).normalized;
    }

    private float NextRange(float min, float max)
    {
        return min + (float)random.NextDouble() * (max - min);
    }

    #region Editor
#if UNITY_EDITOR
    public void DrawGizmos()
    {
        Settings settings = CurrentSettings;
        Vector3 origin = EjectPos;
        float length = Mathf.Max(0.1f, settings.gizmoLength);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(origin, 0.08f);
        Gizmos.DrawLine(origin, origin + GetWorldDirection(settings.baseDirection) * length);

        float minY = Mathf.Min(settings.randomRangeYMin, settings.randomRangeYMax);
        float maxY = Mathf.Max(settings.randomRangeYMin, settings.randomRangeYMax);
        Vector3[] endpoints = new Vector3[8];
        int index = 0;
        for (int vertical = 0; vertical < 2; vertical++)
        {
            for (int horizontal = 0; horizontal < 2; horizontal++)
            {
                for (int depth = 0; depth < 2; depth++)
                {
                    Vector3 offset = new(
                        horizontal == 0 ? -settings.randomRangeX : settings.randomRangeX,
                        vertical == 0 ? minY : maxY,
                        depth == 0 ? -settings.randomRangeZ : settings.randomRangeZ);
                    endpoints[index++] = origin + GetWorldDirection(settings.baseDirection + offset) * length;
                }
            }
        }

        Gizmos.color = Color.cyan;
        foreach (Vector3 endpoint in endpoints)
            Gizmos.DrawLine(origin, endpoint);
        for (int corner = 0; corner < endpoints.Length; corner++)
        {
            for (int axis = 1; axis <= 4; axis *= 2)
            {
                int neighbor = corner ^ axis;
                if (corner < neighbor)
                    Gizmos.DrawLine(endpoints[corner], endpoints[neighbor]);
            }
        }
    }
#endif
    #endregion
}
