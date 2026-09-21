using System.Collections;
using UnityEngine;

[RequireComponent(typeof(CatchableObj))]
public class IngredientDamageController : MonoBehaviour
{
    private static readonly float DamageThresholdRatio = 0.3f;
    public static float MaxThrowSpeed { get; private set; } = 0;
    private static float MinDamageSpeed => MaxThrowSpeed * DamageThresholdRatio;

    [SerializeField] private CatchableObj catchable;
    [SerializeField] private string playerTag = "Player";

    private float impactSpeed;
    private IngredientStat stat;
    private bool canDamage = true;
    private Coroutine damageRestoreCoroutine;
    private int damage => stat.damage;

    #region Unity Methods
    private void FixedUpdate()
    {
        impactSpeed = catchable.Rb.linearVelocity.magnitude;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!canDamage) return;
        if (!collision.gameObject.CompareTag(playerTag)) return;
        if (stat == null) SetIngredinetStat();

        PlayerBrain brain = collision.gameObject.GetComponent<PlayerBrain>();
        if (MaxThrowSpeed == 0) MaxThrowSpeed = brain.ThrowForce;

        int tempDmg = CalculateDamage();
        float healthBefore = brain.Health.CurHealth;
        brain.Health.TakeDamage(tempDmg);
        float healthAfter = brain.Health.CurHealth;
    }

    private void OnDisable()
    {
        if (damageRestoreCoroutine != null)
        {
            StopCoroutine(damageRestoreCoroutine);
            damageRestoreCoroutine = null;
        }

        canDamage = true;
    }
    #endregion

    // 지정된 시간 동안 재료의 충돌 피해를 비활성화한다.
    public void DisableDamageFor(float duration)
    {
        if (damageRestoreCoroutine != null)
            StopCoroutine(damageRestoreCoroutine);

        canDamage = false;
        damageRestoreCoroutine = StartCoroutine(RestoreDamageRoutine(duration));
    }

    // 대기 시간이 지나면 재료의 충돌 피해를 다시 활성화한다.
    private IEnumerator RestoreDamageRoutine(float duration)
    {
        yield return new WaitForSeconds(duration);
        canDamage = true;
        damageRestoreCoroutine = null;
    }

    private void SetIngredinetStat()
    {
        Ingredient ingredient = catchable.Data as Ingredient;
        stat = DataManager.Instance.GetIngredientStat().GetData(ingredient.statID);
    }

    private int CalculateDamage()
    {
        if (impactSpeed <= MinDamageSpeed) return 0;
        return Mathf.RoundToInt(damage * impactSpeed / MaxThrowSpeed);
    }
}
