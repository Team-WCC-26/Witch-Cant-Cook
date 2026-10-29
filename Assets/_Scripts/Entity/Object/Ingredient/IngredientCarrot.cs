using System;
using UnityEngine;
using UnityEngine.Events;

[Serializable]
public class CarrotRocketSettings
{
    [Range(0.05f, 3f)] public float rocketDuration = 3f;
    [Tooltip("발사 시작 시 바닥에서 띄우는 높이(m).")]
    [Min(0f)] public float rocketLaunchHeight = 0.35f;
    [Min(0.1f)] public float rocketSpeed = 12f;
    [Tooltip("진행 방향의 회전 속도(도/초).")]
    [Min(0f)] public float rocketTurnSpeed = 120f;
    [Tooltip("발사 방향을 기준으로 허용하는 최대 조향각(도).")]
    [Range(0f, 180f)] public float rocketTurnMaxAngle = 75f;
    [Min(0f)] public float rocketImpactForce = 8f;
    public LayerMask collisionMask = ~0;
}

[RequireComponent(typeof(CatchableObj), typeof(IngredientReaction))]
public class IngredientCarrot : IngredientTrait, IHeldPrimaryAction, IHeldSecondaryAction
{
    [Header("References")]
    [SerializeField] private CatchableObj catchable;
    [SerializeField] private IngredientReaction reaction;

    [Header("Rocket")]
    [SerializeField] private CarrotRocketSettings rocketSettings = new();

    [Header("Optional Flight Effects")]
    [Tooltip("추후 당근 뒤쪽에 배치한 불꽃/연기 파티클을 연결한다.")]
    [SerializeField] private ParticleSystem propulsionParticles;
    [SerializeField] private AudioSource propulsionAudio;

    [Header("Flight Events")]
    [SerializeField] private UnityEvent onFlightStarted = new();
    [SerializeField] private UnityEvent onFlightStopped = new();

    private PlayerRocketController pilot;
    private bool presentingFlight;

    public CatchableObj Catchable => catchable;
    public CarrotRocketSettings Settings => rocketSettings;
    public bool CanRocket => IsTraitAvailable && reaction != null &&
                             !reaction.IsActionCompleted(IngredientAction.Cut);

    private void Awake()
    {
        if (catchable == null) catchable = GetComponent<CatchableObj>();
        if (reaction == null) reaction = GetComponent<IngredientReaction>();
    }

    protected override void StartTrait()
    {
        catchable.OnDropped += OnDropped;
        SetFlightPresentation(false);
    }

    protected override void StopTrait()
    {
        catchable.OnDropped -= OnDropped;
        StopRocket();
    }

    public bool TryUsePrimary(PlayerInteract interact)
    {
        return pilot != null && pilot.IsFlying;
    }

    public bool TryUseSecondary(PlayerInteract interact)
    {
        if (interact.HeldObj != catchable || !CanRocket) return false;

        PlayerBrain holder = catchable.Holder;
        if (holder == null)
            Debug.LogWarning("[CarrotRocket] 발사 실패: 당근을 잡은 플레이어 참조가 없습니다.", this);
        else if (holder.Rocket != null)
            holder.Rocket.TryStartFlight(this);

        // Uncut carrots consume failed/repeated launches instead of accidentally being thrown.
        return true;
    }

    public float GetImpactDamage()
    {
        if (catchable.Data is not Ingredient ingredient) return 0f;
        DataManager manager = DataManager.Instance;
        if (manager == null || !manager.IsDataLoaded) return 0f;

        IngredientStat stat = manager.GetIngredientStat().GetData(ingredient.statID);
        return stat != null ? Mathf.Max(0, stat.damage) : 0f;
    }

    public void SetPilot(PlayerRocketController controller)
    {
        pilot = controller;
    }

    public void SetFlightPresentation(bool flying)
    {
        if (propulsionParticles != null)
        {
            if (flying)
            {
                if (!propulsionParticles.isPlaying) propulsionParticles.Play(true);
            }
            else propulsionParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        if (propulsionAudio != null)
        {
            if (flying)
            {
                if (!propulsionAudio.isPlaying) propulsionAudio.Play();
            }
            else propulsionAudio.Stop();
        }

        if (presentingFlight == flying) return;
        presentingFlight = flying;
        if (flying) onFlightStarted.Invoke();
        else onFlightStopped.Invoke();
    }

    private void OnDropped() => StopRocket();

    private void StopRocket()
    {
        if (pilot != null) pilot.StopFlight();
        pilot = null;
        SetFlightPresentation(false);
    }
}
