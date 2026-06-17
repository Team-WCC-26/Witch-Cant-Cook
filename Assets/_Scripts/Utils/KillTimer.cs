using System.Collections;
using UnityEngine;

public class KillTimer : MonoBehaviour
{
    [SerializeField] private float timeToKill = 5f;

    private IEnumerator Start()
    {
        yield return new WaitForSeconds(timeToKill);
        Destroy(gameObject);
    }
}
