using System;
using UnityEngine;

public class Bell : MonoBehaviour
{
    public static event Action OnBellRung;
    public event Action Rung;

    private void OnCollisionEnter(Collision collision)
    {
        Debug.Log("Bell 충돌 발생");
        Rung?.Invoke();
        OnBellRung?.Invoke();
    }
}
