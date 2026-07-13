using System;
using UnityEngine;

public class Bell : MonoBehaviour
{
    public static event Action OnBellRung;

    private void OnCollisionEnter(Collision collision)
    {
        Debug.Log("Bell 충돌 발생");
        OnBellRung?.Invoke();
    }
}
