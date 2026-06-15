using Server;
using System.Collections;
using System.Collections.Generic;
using Unity.Entities.UniversalDelegates;
using UnityEngine;
using UnityEngine.InputSystem;

public class AnythingSpawner : MonoBehaviour
{
    [SerializeField] private GameObject SpawnPos;

    //private void Start()
    //{        
    //    SpawnTool("Knife");
    //    SpawnTool("Plate"); // 동시에 스폰하면 같은 프레임카운트 가져가게됨.. 아귀찮아
    //}
    private void Update()
    {
        // 너무 임시임
        if (Keyboard.current != null && Keyboard.current.f2Key.wasPressedThisFrame)
        {
            SpawnTool("Knife");
        }
        if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame)
        {
            SpawnTool("Plate");
        }

    }

    private void SpawnTool(string tool)
    {
        GameObject go = ObjectPoolManager.Instance.Pop(tool, SpawnPos.transform.position, SpawnPos.transform.rotation);
        Debug.Log(go);
        if (go.TryGetComponent(out CatchableObj catchable))
        {
            catchable.NetworkId = Time.frameCount;
            NetworkObjectRegistry.Instance.Add(catchable.NetworkId, catchable);
            ObjectPoolManager.Instance.activeObjDict.Add(catchable.NetworkId, go);
        }
    }
}