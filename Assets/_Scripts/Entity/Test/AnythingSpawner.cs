using Server;
using System.Collections;
using System.Collections.Generic;
using Unity.Entities.UniversalDelegates;
using UnityEngine;
using UnityEngine.InputSystem;

public class AnythingSpawner : MonoBehaviour
{
    [SerializeField] private GameObject SpawnPos;
    [SerializeField] private List<GameObject> spawnableObjects;

    private IEnumerator Start()
    {
        yield return new WaitForSeconds(1.0f);

        //for (int i = 0; i < spawnableObjects.Count; i++)
        //{
        //    GameObject go = Instantiate(spawnableObjects[i], SpawnPos.transform.position, SpawnPos.transform.rotation);
        //    if (!go.TryGetComponent(out CatchableObj catchable))
        //    {
        //        continue;
        //    }

        //    catchable.NetworkId = i;
        //    NetworkObjectRegistry.Instance.Register(catchable.NetworkId, catchable);
        //    Debug.Log($"Spawned object with NetworkId: {catchable.NetworkId}");
        //}
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.f2Key.wasPressedThisFrame)
        {
            GameObject go = ObjectPoolManager.Instance.Pop("Knife", SpawnPos.transform.position, SpawnPos.transform.rotation);
            Debug.Log(go);
            if (go.TryGetComponent(out CatchableObj catchable))
            {
                catchable.NetworkId = Time.frameCount;
                NetworkObjectRegistry.Instance.Add(catchable.NetworkId, catchable);
                ObjectPoolManager.Instance.activeObjDict.Add(catchable.NetworkId, go);
            }
        }

    }
}