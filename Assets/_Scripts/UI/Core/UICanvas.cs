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

//    private IEnumerator Start()
//    {
//        if (!GameManager.Instance.isInitialized)
//        {
//            //call Manager Scene
//            yield return SceneManager.LoadSceneAsync("DontDestroy", LoadSceneMode.Additive);
//            showTask = new();
//            UIManager.Instance.LoadingScreen.OnLoadingEvent(showTask);

//            //set UIManager-Parents
//            UIManager.SetParents(parents);

//            //init all managers + @
//            GameManager.Instance.InitApp();
//            yield return new WaitUntil(() => GameManager.Instance.isInitialized);
//        }
//        else
//        {
//            showTask = new();
//            UIManager.Instance.LoadingScreen.OnLoadingEvent(showTask);

//            //set UIManager-Parents
//            UIManager.SetParents(parents);
//        }
//#pragma warning disable CS4014
//        UIManager.Show<UILogin>();
//#pragma warning restore CS4014
//        showTask.SetResult(true);

//        //wait until "Game Start" input
//        yield return new WaitUntil(() => GameManager.isGameStart);

//        //load BoardScene
//        StartCoroutine(UIManager.LoadBoardScene());
//    }
}
