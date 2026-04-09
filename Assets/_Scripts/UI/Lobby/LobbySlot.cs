using Protocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public class LobbySlot : SlotBase
{
    public bool BIsPrivate => _roomData.BIsPrivate;

    private RoomData _roomData;

    public void Init(RoomData roomData, Sprite sprite)
    {
        _roomData = roomData;

        InitData(_roomData.Name, sprite);
        SetIconActive(BIsPrivate);
    }
}
