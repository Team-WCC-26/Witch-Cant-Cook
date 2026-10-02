using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(ParticleSystem))]
public sealed class DishCompleteVfx : MonoBehaviour
{
    [SerializeField, InspectorName("실행 시 자동 재생")]
    private bool playOnEnable;

    [SerializeField, InspectorName("재생 완료 시 오브젝트 삭제")]
    private bool destroyAfterPlayback;

    public ParticleSystem Root => GetComponent<ParticleSystem>();
    private Coroutine cleanupRoutine;

    private void OnEnable()
    {
        if (Application.isPlaying && playOnEnable)
            PlayOnce();
    }

    [ContextMenu("한 번 재생")]
    public void PlayOnce()
    {
        StopEffect();
        Root.Play(true);
        if (Application.isPlaying && destroyAfterPlayback)
            cleanupRoutine = StartCoroutine(DestroyWhenFinished());
    }

    [ContextMenu("정지 및 입자 지우기")]
    public void StopEffect()
    {
        if (cleanupRoutine != null)
        {
            StopCoroutine(cleanupRoutine);
            cleanupRoutine = null;
        }
        Root.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private IEnumerator DestroyWhenFinished()
    {
        yield return null;
        while (Root.IsAlive(true))
            yield return null;
        cleanupRoutine = null;
        Destroy(gameObject);
    }

    private void OnDisable()
    {
        if (cleanupRoutine != null)
        {
            StopCoroutine(cleanupRoutine);
            cleanupRoutine = null;
        }
    }
}
