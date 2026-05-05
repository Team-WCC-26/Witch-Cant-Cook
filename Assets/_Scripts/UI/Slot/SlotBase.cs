using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Slot
{
    public class SlotBase : MonoBehaviour
    {
        [Header("Info")]
        [SerializeField] private TMP_Text _name;
        [SerializeField] private Image _icon;

        [Header("Setting")]
        [SerializeField] private int _maxNameLength = 15;

        private void Awake()
        {
            _name.maxVisibleCharacters = _maxNameLength;
        }

        public void Init(SlotDataBase data)
        {
            if (_name)
            {
                if (name.Length > _maxNameLength)
                {
                    name = name[.._maxNameLength];
                }

                _name.text = data.Name;
            }

            if (_icon)
            {
                _icon.sprite = data.Sprite;
            }
        }

        public void SetIconActive(bool active)
        {
            if (_icon == null) return;

            _icon.gameObject.SetActive(active);
        }
    }
}