using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PasswordInput : MonoBehaviour
{
    [SerializeField] private TMP_Text _nameTxt;
    [SerializeField] private TMP_InputField _passwordInput;
    [SerializeField] private Button _confirmButton;

    private string _id;

    public Action<string, string> OnButtonClicked;

    private void OnEnable()
    {
        _confirmButton.onClick.AddListener(ClickConfirmButton);
    }

    private void OnDisable()
    {
        _confirmButton.onClick.RemoveListener(ClickConfirmButton);
    }

    public void InitInfo(string name, string id)
    {
        _nameTxt.text = name;
        _id = id;
        gameObject.SetActive(true);
    }

    private void ClickConfirmButton()
    {
        if (string.IsNullOrEmpty(_passwordInput.text)) return;

        OnButtonClicked?.Invoke(_id, _passwordInput.text);
        gameObject.SetActive(false);
    }
}
