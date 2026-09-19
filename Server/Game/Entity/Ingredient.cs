using Protocol;
using System.Numerics;

namespace Server;

public class Ingredient() : Entity, ICookable, IInteractable
{
    public int IngredientId { get; private set; }
    public IngredientState ProcessState { get; private set; }
    public int Hp { get; private set; }
    public IngredientStatData Stat { get; private set; }
    public IngredientBehaviour? IngredientBehaviour { get; private set; }
    
    internal void InitIngredientId(int id)
    {
        IngredientId = id;
        Hp = 0;
        ProcessState = 0;

        if (ServerContext.Instance.DataBase.TryGetIngredientStatById(IngredientId, out var stat))
        {
            Stat = stat;
        }

        IngredientBehaviour = id switch
        {
            _ => null
        };
    }

    public override void Destroy()
    {
        base.Destroy();

        IngredientBehaviour?.Clear();
        IngredientBehaviour = null; // Pool 사용하게 되면 반환
    }

    public bool TryCombine(Ingredient other, out Ingredient result)
    {
        result = null;

        var DB = ServerContext.Instance.DataBase;

        if (!DB.IngredientCombinations.TryGetValue(new(this, other), out var resId))
        {
            resId = 99999;
        }

        result = Room.GenerateIngredient(resId, out var entityId);

        other.Destroy();
        Destroy();

        IngredientCombinePacket packet = new()
        {
            SubjectEntityId = EntityId,
            TargetEntityId = other.EntityId,
            NewEntityId = result.EntityId,
            ResultIngredientId = resId
        };

        Room.BroadCast(PacketSerializer.Serialize(packet, true));

        return true;
    }

    public bool TryCook(IngredientState state)
    {
        if ((ProcessState & state) != 0) return false;

        var DB = ServerContext.Instance.DataBase;

        if ((DB.Ingredients[IngredientId].InvalidProcessFlag & state) != 0) return false;

        ProcessState |= state;
        MakeDirty(DirtyMask.State);

        return true;
    }

    public bool Interact(Player player)
    {
        if (player.HoldingEntity == null)
        {
            player.HoldingEntity = this;
            Parent = player;

            return true;
        }

        if (player.HoldingEntity is Knife knife)
        {
            Hp += knife.Damage;

            if (Hp >= Stat.Hp) return TryCook(IngredientState.Cut);

            MakeDirty(DirtyMask.Process);

            return true;
        }

        if (player.HoldingEntity is Dish dish)
        {
            return dish.TryCombine(this);
        }

        //if (player.HoldingEntity is not ICombinable combinable) return false;

        //TryCombine(combinable, out var res);

        return false;
    }

    public override void WriteSnapShot(WorldStatePacket packet, DirtyMask mask)
    {
        base.WriteSnapShot(packet, mask);

        if (mask.HasFlag(DirtyMask.State))
        {
            packet.CookCompleteIngredients.Add(new()
            {
                ToolEntityId = (Parent == null) ? -1 : Parent.EntityId,
                IngredientEntityId = EntityId,
                CookType = ProcessState
            });
        }
        else if (mask.HasFlag(DirtyMask.Process))
        {
            packet.CookProcessIngredients.Add(new()
            {
                EntityId = EntityId,
                Process = 1.0f * Hp / Stat.Hp
            });
        }
    }

    public void OnPickup(Player player)
    {
        if (IngredientBehaviour is IPickupHandler handler)
        {
            BroadCastResult(handler.OnPickUp(player));
        }
    }

    public void OnDrop()
    {
        if (IngredientBehaviour is IDropHandler handler)
        {
            BroadCastResult(handler.OnDrop());
        }
    }

    public void OnCollision()
    {
        if (IngredientBehaviour is ICollisionHandler handler)
        {
            BroadCastResult(handler.OnCollision());
        }
    }

    private void BroadCastResult(byte[] packet)
    {
        if (packet == null) return;

        Room.BroadCast(packet);
    }
}
