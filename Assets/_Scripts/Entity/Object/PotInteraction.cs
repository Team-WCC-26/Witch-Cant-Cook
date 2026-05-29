using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PotInteraction : MonoBehaviour
{
    private Dictionary<Collision, Coroutine> currentIngredients = new();

    //const values for damage, can be changed later when we have data
    private const int potDmg = 200;
    private const float boilInterval = 1.0f;

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

    private void OnCollisionEnter(Collision collision)
    {
        IngredientReaction reaction = collision.collider.GetComponent<IngredientReaction>();

        if (reaction != null)
        {
            Coroutine boilCoroutine = StartCoroutine(BoilIngredient(reaction));
            currentIngredients.Add(collision, boilCoroutine);
            reaction.Catchable.ChangePickState(false);
        }
    }

    private IEnumerator BoilIngredient(IngredientReaction reaction)
    {
        while (true)
        {
            yield return new WaitForSeconds(boilInterval);
            if (reaction.Interact(IngredientAction.Boil, potDmg))
            {
                //TODO : object pool에서 해당 아이템 disable
                yield break;
            }
        }
    }
}
