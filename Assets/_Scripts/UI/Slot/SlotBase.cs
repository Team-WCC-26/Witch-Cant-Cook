using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SlotBase : MonoBehaviour
{
    [Header("Info")]
    [SerializeField] private TextMeshProUGUI _name;
    [SerializeField] private Image _icon;

    [Header("Setting")]
    [SerializeField] private int _maxNameLength = 15;

    public void InitData(string name, Sprite sprite)
    {
        if (_name)
        {
            if (name.Length > _maxNameLength)
            {
                name = name[.._maxNameLength];
            }

            _name.text = name ;
        }
        
        if (_icon)
        {
            _icon.sprite = sprite;
        }
    }

    public void SetIconActive(bool active)
    {
        if (_icon == null) return;

        _icon.gameObject.SetActive(active);
    }
}
