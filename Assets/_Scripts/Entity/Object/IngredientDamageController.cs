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
    private int damage => stat.damage;

    #region Unity Methods
    private void FixedUpdate()
    {
        impactSpeed = catchable.Rb.linearVelocity.magnitude;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!collision.gameObject.CompareTag(playerTag)) return;
        if (stat == null) SetIngredinetStat();

        PlayerBrain brain = collision.gameObject.GetComponent<PlayerBrain>();
        if (MaxThrowSpeed == 0) MaxThrowSpeed = brain.ThrowForce;

        int tempDmg = CalculateDamage();
        brain.Health.TakeDamage(tempDmg);
    }
    #endregion

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