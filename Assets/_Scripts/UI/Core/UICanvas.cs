using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

public class UICanvas : MonoBehaviour
{
    [SerializeField] private List<Transform> parents = new List<Transform>();
    public static bool isSet { get; private set; }
    private TaskCompletionSource<bool> showTask;

    private void Start()
    {
        if (UIManager.Instance != null)
        {
            UIManager.SetParents(parents);

            UIManager.Show<LobbyRouterUI>();
        }
    }
}
