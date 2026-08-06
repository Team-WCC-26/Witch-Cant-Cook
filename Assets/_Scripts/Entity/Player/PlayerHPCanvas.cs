using UnityEngine;
using UnityEngine.UI;

public class PlayerHPCanvas : MonoBehaviour
{
    [SerializeField] private GameObject hpBackground;
    [SerializeField] private Image hpForeground;

    [SerializeField] private PlayerBrain brain;
    private PlayerHealthData health;

    private void Update()
    {
        BindHealthIfNeeded();
    }

    private void OnDisable()
    {
        if (health != null)
        {
            health.HealthChanged -= Refresh;
        }
    }

    private void BindHealthIfNeeded()
    {
        if (brain == null || health == brain.Health) return;
        if (health != null) health.HealthChanged -= Refresh;

        health = brain.Health;
        if (health == null) return;

        health.HealthChanged += Refresh;
        Refresh(health);
    }

    private void Refresh(PlayerHealthData data)
    {
        hpForeground.fillAmount = data.NormalizedHealth;
        hpBackground.SetActive(data.NormalizedHealth < 1f);
    }

}
