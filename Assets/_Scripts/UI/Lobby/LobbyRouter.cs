using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class LobbyRouter : UIBase
{
    [SerializeField] private GameObject _slotPrefab; // TODO => Pooling system으로 넘기긴 해야할듯
    [SerializeField] private Sprite _privateIcon;

    [SerializeField] private Transform _scrollInner;
    [SerializeField] private Button _refreshButton;

    private List<LobbySlot> _lobbySlots = new(); // 역순으로 보여줌
    private Dictionary<string, LobbySlot> _lobbyDict = new();

    public void CreateLobby(string name, string password) // 서버 응답 받아와야하는데
    {
        var newSlot = Instantiate(_slotPrefab, _scrollInner).GetComponent<LobbySlot>();

        newSlot.gameObject.transform.SetAsFirstSibling();
        newSlot.Init(CreateID(), name, password, _privateIcon); // 아이디 생성방식 필요
        _lobbySlots.Add(newSlot);

    }

    public void DisposeLobby(int index)
    {

    }

    private LobbySlot GetSlot(int index)
    {
        return _lobbySlots[_lobbySlots.Count - index];
    }

    private string CreateID()
    {
        return "";
    }
}
