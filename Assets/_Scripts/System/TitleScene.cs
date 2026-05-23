using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class TitleScene : MonoBehaviour
{
    [SerializeField] private Button BTN_start;
    readonly string mainSceneName = "DirectPlayground";
    void Start()
    {
        BTN_start.onClick.AddListener(OnClickStartBTN);
    }

    void OnClickStartBTN()
    {
        // TODO: 리소스 로드 하고 나서 씬 전환
        SceneManager.LoadScene(mainSceneName);
    }

}
