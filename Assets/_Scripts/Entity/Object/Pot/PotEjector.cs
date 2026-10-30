using System.Collections;
using System.Collections.Generic;
using Protocol;
using UnityEngine;

public sealed class PotEjector : ObjEjector
{
    private sealed class PotRequest : Request
    {
        public PotEjectPacket packet;
        public bool waitPunchEffect;
    }

    private readonly PotInteraction pot;
    private readonly Settings settings = new();

    protected override Settings CurrentSettings
    {
        get
        {
            pot.ConfigureEjection(settings);
            return settings;
        }
    }

    public PotEjector(PotInteraction pot) : base(pot)
    {
        this.pot = pot;
    }

    public void Enqueue(PotEjectPacket packet)
    {
        if (packet == null) return;

        base.Enqueue(new PotRequest
        {
            packet = packet,
            seed = packet.EjectSeed,
            // 솥의 자동 배출 지연은 이미 서버에서 기다린 뒤 패킷을 보낸다.
            delay = 0f
        });
    }

    protected override void OnRequestStarted(Request request)
    {
        PotRequest potRequest = (PotRequest)request;
        potRequest.waitPunchEffect = potRequest.packet.PlayerIds.Count > 0 &&
            !pot.HasCompletedDish;
        pot.BeginEjection();
    }

    protected override IEnumerable<Object> GetTargets(Request request)
    {
        PotEjectPacket packet = ((PotRequest)request).packet;
        long completedDishId = pot.CompletedDishId;
        if (completedDishId != 0 && packet.EntityIds.Contains(completedDishId))
            yield return pot.FindEjectObj(completedDishId);

        foreach (string playerId in packet.PlayerIds)
        {
            PlayerBrain player = null;
            PlayerSpawnManager.Instance?.TryGetPlayer(playerId, out player);
            yield return player;
        }

        foreach (long entityId in packet.EntityIds)
        {
            if (completedDishId != 0 && entityId == completedDishId) continue;
            yield return pot.FindEjectObj(entityId);
        }
    }

    protected override IEnumerator WaitBeforeSequence(Request request)
    {
        return pot.WaitForEjectionVisual(((PotRequest)request).waitPunchEffect);
    }

    protected override float PlayEjectVisual()
    {
        return pot.PlayEjectionVisual();
    }

    protected override void EjectTarget(Object target, Vector3 direction)
    {
        if (target is PlayerBrain player)
        {
            pot.PreparePlayerEjection(player);
            player.ActionController.LaunchRagdoll(EjectPos, direction * pot.PlayerEjectForce);
        }
        else if (target is CatchableObj catchable)
        {
            float force = pot.GetObjEjectForce(catchable);
            pot.PrepareObjEjection(catchable);
            EjectObj(catchable, direction, force);
            catchable.GetComponent<IngredientTraitBounce>()?.WaitForEjectionCollision(pot);
        }
    }

    protected override void OnRequestCompleted()
    {
        pot.FinishEjection();
    }
}
