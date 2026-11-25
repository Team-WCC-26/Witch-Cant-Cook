using System;
using UnityEngine;

namespace Server
{
    // Call Tick on the Unity thread. Completion means a gameplay response, not a successful write.
    public sealed class NetworkResponseWait
    {
        private string operation;
        private long entityId;
        private float startedAt;
        private float timeout;
        private Action onTimeout;
        private bool expired;

        public bool IsWaiting { get; private set; }

        public void Begin(string name, long targetId, float timeoutSeconds, Action timeoutAction)
        {
            operation = name;
            entityId = targetId;
            startedAt = Time.realtimeSinceStartup;
            timeout = Mathf.Max(0.01f, timeoutSeconds);
            onTimeout = timeoutAction;
            expired = false;
            IsWaiting = true;
        }

        public void Tick()
        {
            if (!IsWaiting || Time.realtimeSinceStartup - startedAt < timeout) return;
            IsWaiting = false;
            expired = true;
            Action callback = onTimeout;
            onTimeout = null;
            Error(operation, entityId, $"Response timeout ({Time.realtimeSinceStartup - startedAt:F3}s)");
            callback?.Invoke();
        }

        public bool TryComplete()
        {
            Tick();
            if (!IsWaiting)
            {
                Error(operation, entityId, expired ? "Late response ignored" : "Duplicate or unexpected response ignored");
                return false;
            }
            IsWaiting = false;
            onTimeout = null;
            return true;
        }

        public void Cancel()
        {
            IsWaiting = false;
            expired = false;
            onTimeout = null;
        }

        public static void Error(string operation, long entityId, string reason)
        {
            Debug.LogError($"<color=#D97A7A><size=14><b>[Network] {operation} EntityId={entityId}: {reason}</b></size></color>");
        }
    }
}