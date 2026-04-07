using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public class LobbySlot : BaseSlot
{
    public bool BIsPrivate => string.IsNullOrEmpty(_password);

    private string _id;
    private string _name;
    private string _password;

    public void Init(string id, string name, string password, Sprite sprite)
    {
        _name = name;
        _password = password;

        InitData(name, sprite);
        SetIconActive(BIsPrivate);
    }
}
