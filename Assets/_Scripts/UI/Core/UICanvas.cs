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

    #region Unity Callbacks
    private IEnumerator Start()
    {
        if (gameObject.scene.name == "MainScene" && GetComponentInChildren<DemoCompleteUI>(true) == null)
        {
            GameObject demoComplete = Resources.Load<GameObject>("UI/DemoCompleteUI");
            if (demoComplete != null) Instantiate(demoComplete, transform, false);
            else Debug.LogError("DemoCompleteUI prefab is missing from Resources/UI.", this);
        }

        if (UIManager.Instance == null) yield break;

        UIManager.SetParents(parents);

        // GameManager owns preloading; show the initial UI after its assets are cached.
        yield return new WaitUntil(() => ResourceManager.Instance != null && ResourceManager.Instance.IsPreloaded);

        if (UIManager.Instance == null) yield break;
        _ = UIManager.Show<LobbyRouterUI>();
    }
    #endregion
}
