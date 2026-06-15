using System.Collections.Generic;
using UnityEngine;

public class SubmitTriggerZone : MonoBehaviour
{
    private readonly HashSet<PlateInteraction> _plates = new(); // ¿Ö »ç¿ë? 
    
    void OnEnable()
    {
        Bell.OnBellRung += Submit;
    }

    void OnDisable()
    {
        Bell.OnBellRung -= Submit;
    }
    private void OnTriggerEnter(Collider other)
    {
        if (other.TryGetComponent(out PlateInteraction plate))
        {
            _plates.Add(plate);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.TryGetComponent(out PlateInteraction plate))
        {
            _plates.Remove(plate);
        }
    }

    private void Submit()
    {
        var plates = new List<PlateInteraction>(_plates);

        foreach (var plate in plates)
        {
            SubmitPlate(plate);
        }

        _plates.Clear();
    }

    private void SubmitPlate(PlateInteraction plate)
    {
        Transform plateTransform = plate.transform;

        for (int i = plateTransform.childCount - 1; i >= 0; i--)
        {
            Transform child = plateTransform.GetChild(i);

            ObjectPoolManager.Instance.Push(child.gameObject);
        }

        ObjectPoolManager.Instance.Push(plate.gameObject);
    }
}
