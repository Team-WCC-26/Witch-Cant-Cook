using Server;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AnythingSpawner : MonoBehaviour
{
    [SerializeField] private GameObject SpawnPos;
    [SerializeField] private List<GameObject> spawnableObjects;

    private IEnumerator Start()
    {
        yield return new WaitForSeconds(1.0f);

        for (int i = 0; i < spawnableObjects.Count; i++)
        {
            GameObject go = Instantiate(spawnableObjects[i], SpawnPos.transform.position, SpawnPos.transform.rotation);
            if (!go.TryGetComponent(out CatchableObj catchable))
            {
                continue;
            }

            catchable.NetworkId = i;
            GameManager.Instance.catchableDics.Add(catchable.NetworkId, catchable);
            Debug.Log($"Spawned object with NetworkId: {catchable.NetworkId}");
        }
    }
}