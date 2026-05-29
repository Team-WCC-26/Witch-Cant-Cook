using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class OvenInteraction : MonoBehaviour
{
    private Dictionary<Collider, Coroutine> currentIngredients = new();

    //const values for damage, can be changed later when we have data
    private const int ovenDmg = 200;
    private const float cookInterval = 1.0f;

    private void OnDestroy()
    {
        foreach (var kvp in currentIngredients)
        {
            if (kvp.Key != null)
            {
                StopCoroutine(kvp.Value);
            }
        }
        currentIngredients.Clear();
    }

    private void OnTriggerEnter(Collider other)
    {
        IngredientReaction reaction = other.GetComponent<IngredientReaction>();

        if (reaction != null)
        {
            Coroutine cookCoroutine = StartCoroutine(CookIngredient(reaction));
            currentIngredients.Add(other, cookCoroutine);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (currentIngredients.TryGetValue(other, out Coroutine cookCoroutine))
        {
            StopCoroutine(cookCoroutine);
            currentIngredients.Remove(other);
        }
    }

    //TODO : 아마? 시간의 흐름 서버가 계산하도록 권한 이전
    private IEnumerator CookIngredient(IngredientReaction reaction)
    {
        while (true)
        {
            yield return new WaitForSeconds(cookInterval);
            if (reaction.Interact(IngredientAction.Cook, ovenDmg))
            {
                yield break;
            }
        }
    }
}
