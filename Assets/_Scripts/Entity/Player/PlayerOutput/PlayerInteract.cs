using Protocol;
using Server;
using UnityEngine;

public class PlayerInteract
{
    private enum PendingAttackImpact { None, Punch, Knife }
    private const float RemotePunchRadiusBonus = 0.15f;
    private const float RemotePunchDistanceBonus = 0.25f;

    private readonly PlayerBrain brain;
    private readonly PhysicsCastProbe[] interactionCasts;
    private PendingAttackImpact pendingAttackImpact;
    private CatchableObj pendingKnife;
    private float pendingImpactDeadline;
    private CatchableObj preparedThrow;
    private uint throwReleaseVersion;

    public CatchableObj HeldObj { get; private set; }
    public bool IsHolding => HeldObj != null;

    public PlayerInteract(PlayerBrain brain)
    {
        this.brain = brain;
        interactionCasts = brain.InteractionCasts;
    }

    public void Handle(PlayerInteraction interaction)
    {
        foreach (PhysicsCastProbe probe in interactionCasts)
            if (probe != null) probe.DrawDebug();

        if (!PlayerSpawnManager.Instance.IsMine(brain.PlayerId)) return;
        if (brain.Rocket != null && brain.Rocket.IsFlying) return;

        switch (interaction)
        {
            case PlayerInteraction.DefaultPrimary:
                RequestDefaultPrimaryAction();
                break;
            case PlayerInteraction.HeldPrimary:
                RequestHeldPrimaryAction();
                break;
            case PlayerInteraction.Secondary:
                RequestSecondaryAction();
                break;
            case PlayerInteraction.KeyInteract:
                RequestKeyInteract();
                break;
        }
    }

    #region Request User Input Action
    private void RequestDefaultPrimaryAction()
    {
        if (IsHolding) return;

        IInteractTarget target = FindInteractTarget();
        if (target is not CatchableObj obj)
        {
            if (brain.ActionController.PunchAction())
            {
                pendingAttackImpact = PendingAttackImpact.Punch;
                pendingImpactDeadline = Time.time + 2f;
            }
            return;
        }
        if (obj.IsHold) return;
        if (!obj.CanBePicked) return;

        RequestEntityInteract(obj.NetworkId);
    }

    private void RequestHeldPrimaryAction()
    {
        if (!IsHolding) return;

        IInteractTarget target = FindInteractTarget();
        OvenButton ovenButton = target.GetTargetComponent<OvenButton>();
        if (ovenButton != null)
        {
            brain.ActionController.PunchAction();
            ovenButton.OnPunched(brain);
            return;
        }

        if (TryUseHeldPrimaryAction()) return;
        if (TryUseHeldObjectReceiver()) return;

        RequestDrop();
    }

    private bool TryUseHeldPrimaryAction()
    {
        if (TryGetHeldComponent(out IHeldPrimaryAction action))
            return action.TryUsePrimary(this);

        return false;
    }

    private bool TryUseHeldObjectReceiver()
    {
        IInteractTarget target = FindInteractTarget();
        IHeldObjectReceiver receiver = target.GetTargetComponent<IHeldObjectReceiver>();
        if (receiver == null) return false;

        return receiver.TryReceiveHeldObject(HeldObj, this);
    }
    
    private void RequestSecondaryAction()
    {
        if (TryGetHeldComponent(out IHeldSecondaryAction action) && action.TryUseSecondary(this)) return;
        if (!IsHolding) return;

        preparedThrow = HeldObj;
        throwReleaseVersion = brain.Input.SecondaryReleaseVersion;
        // Press and release may arrive within the same frame.
        if (!brain.Input.IsSecondaryHeld) RequestThrow();
    }

    private void RequestKeyInteract()
    {
        EntityCategory category = IsHolding ? HeldObj.Category : EntityCategory.Player;

        switch (category)
        {
            case EntityCategory.Player:
                //TODO : 빈손 F키 상호작용 처리 필요
                break;
            case EntityCategory.Ingredient:
                //TODO : 재료 F키 상호작용 처리 필요
                break;
            case EntityCategory.Pan:
                //TODO : 프라이팬 F키 상호작용 처리 필요
                break;
            case EntityCategory.Knife:
                //TODO : 칼 F키 상호작용 처리 필요
                break;
            case EntityCategory.Plate:
                //TODO : 그릇 F키 상호작용 처리 필요
                break;
            case EntityCategory.Broom:
                //TODO : 빗자루 F키 상호작용 처리 필요
                break;
            case EntityCategory.Bucket:
                //TODO : 양동이 F키 상호작용 처리 필요
                break;
        }
    }
    #endregion

    #region User Input Helper
    private void RequestDrop()
    {
        RequestDropHeld();
    }

    public void RequestDropHeld()
    {
        CancelThrowPreparation();
        if (!IsHolding) return;

        brain.Rocket?.StopFlight();

        CatchableObj target = HeldObj;
        EntityThrowPacket packet = new()
        {
            EntityId = target.NetworkId,
            Position = ProtocolTypeConverter.ToNumericsVector3(target.transform.position),
            Velocity = System.Numerics.Vector3.Zero
        };

        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }

    public void ForceDropHeld()
    {
        RequestDropHeld();
    }

    private void RequestThrow()
    {
        CancelThrowPreparation();
        brain.ActionController.CancelAction();
        if (!IsHolding) return;

        CatchableObj target = HeldObj;

        GetThrowLaunch(target, out Vector3 throwPosition, out Vector3 velocity);

        EntityThrowPacket packet = new()
        {
            EntityId = target.NetworkId,
            Position = ProtocolTypeConverter.ToNumericsVector3(throwPosition),
            Velocity = ProtocolTypeConverter.ToNumericsVector3(velocity)
        };

        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }

    public void RequestEntityInteract(long targetEntityId)
    {
        EntityInteractPacket packet = new()
        {
            TargetEntityId = targetEntityId
        };

        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }

    // Arm one impact for each remote punch animation; duplicate animation events are ignored.
    public void QueueRemotePunchImpact()
    {
        if (brain.StateResolver is not RemotePlayerStateResolver) return;
        pendingAttackImpact = PendingAttackImpact.Punch;
        pendingKnife = null;
        pendingImpactDeadline = Time.time + 2f;
    }

    public void QueueKnifeImpact()
    {
        SoundManager.GetOrCreate().Play(SoundId.KnifeSwing, brain.transform.position);
        pendingAttackImpact = PendingAttackImpact.Knife;
        pendingKnife = HeldObj;
        pendingImpactDeadline = Time.time + 2f;
    }

    public void CancelPendingAttackImpact()
    {
        pendingAttackImpact = PendingAttackImpact.None;
        pendingKnife = null;
        pendingImpactDeadline = 0f;
    }

    public void OnPunchImpact()
    {
        if (!TryConsumeAttackImpact(PendingAttackImpact.Punch, out _)) return;

        if (TryFindPunchedPlayer(BuildInteractRay(), GetInteractRadius(), GetInteractDistance()) is PlayerHitWobble playerHit)
        {
            ApplyPlayerPunch(playerHit);
            return;
        }

        IInteractTarget target = FindInteractTarget();
        if (target is CatchableObj) return;
        target?.GetTargetComponent<IPunchReceiver>()?.OnPunched(brain);
    }

    public void OnRemotePunchImpact()
    {
        if (brain.StateResolver is not RemotePlayerStateResolver ||
            pendingAttackImpact != PendingAttackImpact.Punch) return;
        bool valid = Time.time <= pendingImpactDeadline &&
            brain.StateResolver.CurrentState.PhysicalMode == PlayerPhysicalMode.Default &&
            !StageReturnFlow.IsInputBlocked;
        CancelPendingAttackImpact();
        if (!valid) return;

        Transform facing = brain.FacingRoot != null ? brain.FacingRoot : brain.transform;
        Vector3 origin = brain.Col != null ? brain.Col.bounds.center : facing.position + Vector3.up;
        PlayerHitWobble playerHit = TryFindPunchedPlayer(
            new Ray(origin, facing.forward),
            GetInteractRadius() + RemotePunchRadiusBonus,
            GetInteractDistance() + RemotePunchDistanceBonus);
        ApplyPlayerPunch(playerHit);
    }

    private void ApplyPlayerPunch(PlayerHitWobble playerHit)
    {
        if (playerHit == null) return;
        SoundManager.GetOrCreate().Play(SoundId.Punch, playerHit.transform.position);
        playerHit.PlayHit(brain.transform.position);

        PlayerBrain target = playerHit.GetComponent<PlayerBrain>();
        if (target != null && target != brain &&
            target.StateResolver is LocalPlayerStateResolver)
            target.Health?.TakeDamage(brain.PunchDamage);
    }

    private PlayerHitWobble TryFindPunchedPlayer(Ray ray, float radius, float distance)
    {
        RaycastHit[] hits = Physics.SphereCastAll(
            ray.origin,
            radius,
            ray.direction,
            distance,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore);

        float nearestDistance = float.PositiveInfinity;
        Collider nearestCollider = null;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.transform.IsChildOf(brain.transform)) continue;
            if (hit.distance >= nearestDistance) continue;
            nearestDistance = hit.distance;
            nearestCollider = hit.collider;
        }

        if (nearestCollider == null) return null;
        PlayerBrain target = nearestCollider.GetComponentInParent<PlayerBrain>();
        return target != null && target != brain
            ? target.GetComponent<PlayerHitWobble>()
            : null;
    }

    public void OnKnifeImpact()
    {
        if (!TryConsumeAttackImpact(PendingAttackImpact.Knife, out CatchableObj knife)) return;
        if (HeldObj != knife || knife == null) return;

        IInteractTarget impactTarget = FindInteractTarget();
        Bell bell = impactTarget.GetTargetComponent<Bell>();
        if (bell != null)
        {
            bell.Ring();
            return;
        }

        if (impactTarget is not CatchableObj target) return;
        if (!target.TryGetComponent(out IngredientReaction _)) return;
        RequestEntityInteract(target.NetworkId);
    }

    private bool TryConsumeAttackImpact(PendingAttackImpact expected, out CatchableObj knife)
    {
        knife = pendingKnife;
        if (pendingAttackImpact != expected) return false;

        bool valid = Time.time <= pendingImpactDeadline &&
            PlayerSpawnManager.Instance != null &&
            PlayerSpawnManager.Instance.IsMine(brain.PlayerId) &&
            brain.StateResolver.CurrentState.PhysicalMode == PlayerPhysicalMode.Default;
        CancelPendingAttackImpact();
        return valid;
    }

    public bool TryUseEquipment()
    {
        if (!IsHolding) return false;
        if (!HeldObj.IsEquipment) return false;

        return brain.ActionController.TryEquipAction();
    }

    public bool TryReleaseHeld(CatchableObj target)
    {
        if (target == null) return false;
        if (HeldObj != target) return false;
        CancelThrowPreparation();

        brain.Rocket?.StopFlight();

        brain.ActionController.CancelAction();
        HeldObj = null;
        target.transform.SetParent(null, true);
        return true;
    }

    private bool TryGetHeldComponent<T>(out T component) where T : class
    {
        component = null;

        if (!IsHolding) return false;

        foreach (MonoBehaviour behaviour in HeldObj.GetComponents<MonoBehaviour>())
        {
            if (behaviour is not T target) continue;

            component = target;
            return true;
        }

        return false;
    }
    #endregion

    #region Actual Interaction
    public void ApplyPicked(CatchableObj target)
    {
        if (target == null) return;
        CancelThrowPreparation();

        brain.Rocket?.StopFlight();

        brain.ActionController.CancelAction();
        target.OnPick(brain);
        Transform holdParent = target.ResolveHoldParent(brain);

        target.transform.SetParent(holdParent, false);
        LocalTransformData holdTransform = target.HoldTransform;
        target.transform.localPosition = holdTransform.LocalPosition;
        target.transform.localRotation = Quaternion.Euler(holdTransform.LocalEulerAngles);
        HeldObj = target;
    }

    public void ApplyThrown(CatchableObj target)
    {
        if (target == null || HeldObj != target) return;
        CancelThrowPreparation();

        brain.Rocket?.StopFlight();

        brain.ActionController.CancelAction();
        HeldObj = null;
    }
    #endregion

    #region Throw Action Helper
    public void CancelThrowPreparation()
    {
        preparedThrow = null;
        brain.ThrowTrajectory?.Hide();
    }

    public void UpdateThrowPreparation()
    {
        if (preparedThrow == null || HeldObj != preparedThrow ||
            brain.StateResolver is not LocalPlayerStateResolver || !brain.Input.isActiveAndEnabled ||
            brain.Input.IsInputBlocked || brain.StateResolver.CurrentState.PhysicalMode != PlayerPhysicalMode.Default ||
            (brain.Rocket != null && brain.Rocket.IsFlying))
        {
            CancelThrowPreparation();
            return;
        }

        if (brain.Input.SecondaryReleaseVersion != throwReleaseVersion)
        {
            RequestThrow();
            return;
        }
        if (!brain.Input.IsSecondaryHeld)
        {
            CancelThrowPreparation();
            return;
        }
        GetThrowLaunch(preparedThrow, out Vector3 startPos, out Vector3 velocity);
        brain.ThrowTrajectory.Show(preparedThrow, startPos, velocity);
    }

    private void GetThrowLaunch(CatchableObj target, out Vector3 startPos, out Vector3 velocity)
    {
        startPos = GetThrowPosition(target);
        velocity = GetThrowDirection(GetThrowOrigin()) * GetThrowForce();
    }

    private Transform GetThrowOrigin()
    {
        return brain.PlayerCam != null
            ? brain.PlayerCam.transform
            : brain.transform;
    }

    private Vector3 GetThrowPosition(CatchableObj target)
    {
        return brain.ThrowPoint != null
            ? brain.ThrowPoint.transform.position
            : target.transform.position;
    }

    private Vector3 GetThrowDirection(Transform origin)
    {
        float pitchAngle = brain.ThrowAngle;
        if (brain.UseDownwardThrowCompensation)
        {
            float lookDownAngle = Mathf.Asin(Mathf.Clamp(-origin.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            float startAngle = Mathf.Max(0f, brain.DownwardThrowStartAngle);
            float fullAngle = Mathf.Max(startAngle + 0.01f, brain.DownwardThrowFullAngle);
            float correction = Mathf.InverseLerp(startAngle, fullAngle, lookDownAngle);
            pitchAngle = Mathf.Lerp(pitchAngle, brain.DownwardThrowFinalAngle, correction);
        }

        Quaternion pitchOffset = Quaternion.AngleAxis(-pitchAngle, origin.right);
        Quaternion yawOffset = Quaternion.AngleAxis(brain.ThrowYawAngle, Vector3.up);
        return (yawOffset * pitchOffset * origin.forward).normalized;
    }

    private float GetThrowForce()
    {
        return brain.ThrowForce;
    }
    #endregion

    #region Find Interactable Target
    public IInteractTarget FindInteractTarget()
    {
        foreach (PhysicsCastProbe probe in interactionCasts)
        {
            if (probe == null) continue;
            RaycastHit[] hits = probe.Cast(out Ray ray);
            IInteractTarget target = SelectInteractTarget(probe, ray, hits);
            if (target != null) return target;
        }
        return null;
    }

    private IInteractTarget SelectInteractTarget(PhysicsCastProbe probe, Ray ray, RaycastHit[] hits)
    {
        IInteractTarget bestTarget = null;
        float bestScore = float.PositiveInfinity;
        float bestDistance = float.PositiveInfinity;

        foreach (RaycastHit hit in hits)
        {
            Collider hitCollider = hit.collider;
            if (hitCollider.transform.IsChildOf(brain.transform)) continue;
            IInteractTarget target = GetInteractTargetInParent(hitCollider);
            if (target == null || !target.Accepts(GetSourceCategory())) continue;

            float score = probe.Type == PhysicsCastProbe.CastType.Raycast
                ? hit.distance
                : GetTargetScore(probe, ray, hitCollider, hit.distance);
            if (score > bestScore || (score == bestScore && hit.distance >= bestDistance)) continue;

            bestTarget = target;
            bestScore = score;
            bestDistance = hit.distance;
        }
        return bestTarget;
    }

    private EntityCategory GetSourceCategory()
    {
        return HeldObj != null ? HeldObj.Category : EntityCategory.Player;
    }

    private static float GetTargetScore(PhysicsCastProbe probe, Ray ray, Collider targetCollider, float distance)
    {
        Vector3 targetCenter = targetCollider.bounds.center;
        Vector3 castCenter = ray.GetPoint(Mathf.Max(0f, Vector3.Dot(targetCenter - ray.origin, ray.direction)));
        float offset = Vector3.Distance(castCenter, targetCenter);
        float offsetScore = Mathf.Clamp01(offset / Mathf.Max(0.01f, probe.Radius));
        float distanceRatio = Mathf.Clamp01(distance / probe.Distance);
        int distanceScore = Mathf.Min(Mathf.FloorToInt(distanceRatio * 3f), 2);
        return offsetScore + distanceScore;
    }

    private static IInteractTarget GetInteractTargetInParent(Collider collider)
    {
        foreach (MonoBehaviour behaviour in collider.GetComponentsInParent<MonoBehaviour>())
        {
            if (behaviour is IInteractTarget target) return target;
        }
        return null;
    }
    #endregion

    #region Player Hit Query Settings
    private Ray BuildInteractRay()
    {
        if (brain.CameraInteractionCast != null) return brain.CameraInteractionCast.BuildRay();
        Transform origin = brain.PlayerCam != null ? brain.PlayerCam.transform : brain.transform;
        return new Ray(origin.position + origin.TransformDirection(brain.InteractRayStartOffset), origin.forward);
    }

    private float GetInteractRadius()
    {
        return Mathf.Max(0.01f, brain.CameraInteractionCast != null
            ? brain.CameraInteractionCast.Radius : brain.InteractRadius);
    }

    private float GetInteractDistance()
    {
        return brain.CameraInteractionCast != null
            ? brain.CameraInteractionCast.Distance : brain.InteractDistance;
    }
    #endregion
}
