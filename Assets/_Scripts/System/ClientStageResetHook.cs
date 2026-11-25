using UnityEngine;
using UnityEngine.Events;

public sealed class ClientStageResetHook : MonoBehaviour, IClientStageResettable
{
    [Tooltip("바닥 오염 제거, 이벤트 종료 등 클라이언트 초기화 함수를 연결합니다.")]
    [SerializeField] private UnityEvent resetStage = new();
    public void ResetClientStage() => resetStage.Invoke();
}
