using System.Collections.Generic;
using DG.Tweening;
using Protocol;
using Server;
using UnityEngine;

[DisallowMultipleComponent]
public class JumpAssist : MapObjInteraction
{
    [Header("발사 대기")]
    [SerializeField, Min(0.01f)] private float activationDelay = 1f;
    [SerializeField] private Ease pressEase = Ease.Linear;

    [Header("발사")]
    [SerializeField, Range(1f, 89f)] private float launchAngle = 60f;
    [SerializeField, Min(0.01f)] private float launchForce = 12f;
    [SerializeField, Range(-180f, 180f)] private float liftAngle = 60f;
    [SerializeField, Min(0.01f)] private float liftDuration = 0.12f;
    [SerializeField] private Ease liftEase = Ease.OutCubic;

    [Header("복귀")]
    [SerializeField, Min(0f)] private float holdDuration = 0.1f;
    [SerializeField, Min(0.01f)] private float returnDuration = 0.8f;
    [SerializeField] private Ease returnEase = Ease.InOutSine;
    [SerializeField, Min(0.01f)] private float cancelReturnDuration = 0.1f;
    [SerializeField] private Ease cancelReturnEase = Ease.OutSine;

    private enum PadState { Idle, Waiting, Launching, Returning }

    private sealed class Flight
    {
        public PlayerBrain player;
        public bool previousMovementBlocked;
        public bool launched;
    }

    private Quaternion restRotation;
    private Sequence motion;
    private PadState state;
    private bool localPresenceReported;
    private bool serverReady = true;
    private int lastFireRound;
    private readonly Dictionary<Collider, PlayerBrain> occupants = new();
    private readonly Dictionary<Collider, CatchableObj> ingredients = new();
    private readonly HashSet<CatchableObj> launchedIngredients = new();
    private readonly List<Collider> staleContacts = new();
    private readonly List<Flight> flights = new();

    #region Unity Callbacks

    private void Awake()
    {
        restRotation = transform.localRotation;
    }

    private void OnTriggerStay(Collider other)
    {
        CatchableObj ingredient = other.GetComponentInParent<CatchableObj>();
        if (IsLaunchableIngredient(ingredient)) ingredients[other] = ingredient;
        else ingredients.Remove(other);

        PlayerBrain player = other.GetComponentInParent<PlayerBrain>();
        if (player == null || other != player.Col || !IsStanding(player))
        {
            occupants.Remove(other);
            return;
        }
        occupants[other] = player;
    }

    private void OnTriggerExit(Collider other)
    {
        occupants.Remove(other);
        ingredients.Remove(other);
    }

    private void Update()
    {
        staleContacts.Clear();
        foreach (var contact in occupants)
        {
            if (contact.Key == null || !contact.Key.enabled || !contact.Key.gameObject.activeInHierarchy
                || !IsStanding(contact.Value)) staleContacts.Add(contact.Key);
        }
        foreach (Collider contact in staleContacts) occupants.Remove(contact);

        staleContacts.Clear();
        foreach (var contact in ingredients)
        {
            if (contact.Key == null || !contact.Key.enabled || !contact.Key.gameObject.activeInHierarchy
                || !IsLaunchableIngredient(contact.Value)) staleContacts.Add(contact.Key);
        }
        foreach (Collider contact in staleContacts) ingredients.Remove(contact);

        bool hasLocalPlayer = false;
        foreach (PlayerBrain player in occupants.Values)
        {
            if (player.StateResolver is LocalPlayerStateResolver) hasLocalPlayer = true;
        }
        // Each client reports one balanced enter/exit pair for its combined presence.
        SendPresence(hasLocalPlayer || ingredients.Count > 0);

        for (int i = flights.Count - 1; i >= 0; i--)
        {
            Flight flight = flights[i];
            if (!IsAvailable(flight.player)
                || (flight.launched && !flight.player.ActionController.Movement.IsEjectionActive))
            {
                RestorePlayer(flight);
                flights.RemoveAt(i);
            }
        }

        if (state == PadState.Waiting && occupants.Count == 0 && ingredients.Count == 0) CancelWaiting();
        else if (state == PadState.Idle && IsRegistered && serverReady && (occupants.Count > 0 || ingredients.Count > 0)) BeginWaiting();
    }

    private void OnDisable()
    {
        SendPresence(false);
        motion?.Kill();
        motion = null;
        foreach (Flight flight in flights) RestorePlayer(flight);
        flights.Clear();
        occupants.Clear();
        ingredients.Clear();
        launchedIngredients.Clear();
        state = PadState.Idle;
        serverReady = true;
        if (Application.isPlaying) transform.localRotation = restRotation;
    }

    #endregion

    public void HandleJumpPadEvent(JumpPadEventPacket packet)
    {
        switch (packet.EventType)
        {
            case JumpPadEventType.Fire:
                if (packet.FireRound <= lastFireRound) return;
                lastFireRound = packet.FireRound;
                serverReady = false;
                motion?.Kill();
                state = PadState.Launching;
                PreparePlayers();
                LaunchIngredients();
                motion = CreateMotion(restRotation);
                motion.Goto(Mathf.Max(0.01f, activationDelay), true);
                motion.OnComplete(FinishMotion);
                break;
            case JumpPadEventType.Cancel:
                serverReady = false;
                CancelWaiting();
                break;
            case JumpPadEventType.Return:
                serverReady = true;
                break;
        }
    }

    public void PlayMotion()
    {
        if (!Application.isPlaying || !isActiveAndEnabled || state != PadState.Idle) return;
        state = PadState.Launching;
        motion = CreateMotion(restRotation);
        motion.OnComplete(FinishMotion);
    }

    private void SendPresence(bool entered)
    {
        if (localPresenceReported == entered || !IsRegistered || ServerManager.Instance == null) return;
        localPresenceReported = entered;
        if (entered)
            _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(
                new JumpPadPlayerEnterPacket { PadEntityId = NetworkId }));
        else
            _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(
                new JumpPadPlayerExitPacket { PadEntityId = NetworkId }));
    }

    private static bool IsAvailable(PlayerBrain player)
    {
        return player != null && player.isActiveAndEnabled && player.Col != null
            && player.Rb != null && player.Input != null && player.StateResolver != null
            && player.StateResolver.CurrentState.PhysicalMode == PlayerPhysicalMode.Default;
    }

    private static bool IsLaunchableIngredient(CatchableObj ingredient)
    {
        return ingredient != null && ingredient.isActiveAndEnabled
            && ingredient.Category == EntityCategory.Ingredient && !ingredient.IsHold
            && ingredient.Holder == null && ingredient.ParentEntityId <= 0
            && !ingredient.IsSubmissionPending && !ingredient.IsSubmissionReturning
            && ingredient.Rb != null && !ingredient.Rb.isKinematic;
    }

    private static bool IsStanding(PlayerBrain player)
    {
        return IsAvailable(player) && !player.Input.IsInputBlocked && !player.Input.IsMovementBlocked
            && !player.ActionController.Movement.IsForcedMovementActive;
    }

    private Sequence CreateMotion(Quaternion initialRotation)
    {
        Vector3 pressedAngles = initialRotation.eulerAngles;
        pressedAngles.z = 0f;
        Quaternion pressedRotation = Quaternion.Euler(pressedAngles);
        Quaternion liftedRotation = pressedRotation * Quaternion.Euler(0f, 0f, liftAngle);

        Sequence sequence = DOTween.Sequence();
        sequence.Append(transform.DOLocalRotateQuaternion(pressedRotation, Mathf.Max(0.01f, activationDelay))
            .SetEase(pressEase));
        sequence.Append(transform.DOLocalRotateQuaternion(liftedRotation, Mathf.Max(0.01f, liftDuration))
            .SetEase(liftEase));
        sequence.AppendInterval(Mathf.Max(0f, holdDuration));
        sequence.Append(transform.DOLocalRotateQuaternion(initialRotation, Mathf.Max(0.01f, returnDuration))
            .SetEase(returnEase));
        return sequence;
    }

    private void BeginWaiting()
    {
        state = PadState.Waiting;
        Vector3 pressedAngles = restRotation.eulerAngles;
        pressedAngles.z = 0f;
        motion = DOTween.Sequence();
        motion.Append(transform.DOLocalRotateQuaternion(Quaternion.Euler(pressedAngles),
            Mathf.Max(0.01f, activationDelay)).SetEase(pressEase));
    }

    private void CancelWaiting()
    {
        motion?.Kill();
        state = PadState.Returning;
        motion = DOTween.Sequence();
        motion.Append(transform.DOLocalRotateQuaternion(restRotation, Mathf.Max(0.01f, cancelReturnDuration))
            .SetEase(cancelReturnEase));
        motion.OnComplete(FinishMotion);
    }

    private void PreparePlayers()
    {
        foreach (PlayerBrain player in occupants.Values)
        {
            if (IsStanding(player) && player.Interact != null
                && player.Interact.HeldObj is CatchableObj heldIngredient
                && heldIngredient.Category == EntityCategory.Ingredient)
            {
                if (player.StateResolver is LocalPlayerStateResolver) player.Interact.ForceDropHeld();
                // Release immediately while the server confirms the ownership change.
                if (player.Interact.TryReleaseHeld(heldIngredient)) heldIngredient.OnDrop();
            }

            // Each client launches only its own player.
            if (!IsAvailable(player) || player.Rb.isKinematic
                || player.StateResolver is not LocalPlayerStateResolver
                || player.Input.IsInputBlocked || player.Input.IsMovementBlocked
                || player.ActionController.Movement.IsForcedMovementActive) continue;
            flights.Add(new Flight { player = player, previousMovementBlocked = player.Input.IsMovementBlocked });
            player.Input.IsMovementBlocked = true;
        }
        LaunchPlayers();
    }

    private void LaunchPlayers()
    {
        Quaternion baseRotation = transform.parent != null
            ? transform.parent.rotation * restRotation : restRotation;
        Vector3 horizontal = Vector3.ProjectOnPlane(baseRotation * Vector3.left, Vector3.up).normalized;
        float angle = Mathf.Clamp(launchAngle, 1f, 89f) * Mathf.Deg2Rad;
        Vector3 direction = (horizontal * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle)).normalized;

        foreach (Flight flight in flights)
        {
            if (flight.launched || !IsAvailable(flight.player) || flight.player.Rb.isKinematic) continue;
            Rigidbody rb = flight.player.Rb;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            flight.player.ActionController.Movement.BeginEjectionMovement(float.PositiveInfinity);
            rb.AddForce(direction * Mathf.Max(0.01f, launchForce), ForceMode.Impulse);
            flight.player.ActionController.PlayLaunchJumpStart();
            flight.launched = true;
        }
    }

    private void LaunchIngredients()
    {
        Quaternion baseRotation = transform.parent != null
            ? transform.parent.rotation * restRotation : restRotation;
        Vector3 horizontal = Vector3.ProjectOnPlane(baseRotation * Vector3.left, Vector3.up).normalized;
        float angle = Mathf.Clamp(launchAngle, 1f, 89f) * Mathf.Deg2Rad;
        Vector3 velocity = (horizontal * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle)).normalized
            * Mathf.Max(0.01f, launchForce);

        launchedIngredients.Clear();
        foreach (CatchableObj ingredient in ingredients.Values)
        {
            if (!IsLaunchableIngredient(ingredient) || !launchedIngredients.Add(ingredient)) continue;
            ingredient.Rb.angularVelocity = Vector3.zero;
            ingredient.Rb.linearVelocity = velocity;
        }
    }

    private static void RestorePlayer(Flight flight)
    {
        if (flight.player == null) return;
        if (flight.player.Input != null) flight.player.Input.IsMovementBlocked = flight.previousMovementBlocked;
        flight.player.ActionController.Movement.UpdateEjectionMovement(false);
        flight.player.ActionController.Movement.BeginForcedMovement(0f);
    }

    private void FinishMotion()
    {
        transform.localRotation = restRotation;
        motion = null;
        state = PadState.Idle;
    }

    #region Editor
#if UNITY_EDITOR
    public Sequence CreatePreviewMotion()
    {
        return CreateMotion(transform.localRotation).SetAutoKill(false).Pause();
    }
#endif
    #endregion
}
