using Protocol;
using UI.Slot;

public class LobbySlot : SlotBase
{
    public void Init(LobbySlotData data)
    {
        base.Init(data);
        SetIconActive(data.RoomData.BIsPrivate);
    }
}

public class LobbySlotData : SlotDataBase
{
    public override string Name => RoomData.Name;
    public RoomData RoomData { get; set; }
}
