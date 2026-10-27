using UnityEngine;
using UnityEngine.InputSystem;

// Test shortcut: close the lobby door and disable the kitchen door locally with Tab.
public sealed class TEST_SetStage : MonoBehaviour
{
    private static TEST_SetStage instance;
    private GameObject kitchenDoor;
    private Door lobbyDoor;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateForTestScene()
    {
        if (FindFirstObjectByType<DoorController>() == null || instance != null) return;
        new GameObject(nameof(TEST_SetStage)).AddComponent<TEST_SetStage>();
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        lobbyDoor = GameObject.Find("LobbyDoor")?.GetComponent<Door>();
        kitchenDoor = GameObject.Find("KitchenDoor");
    }

    private void Update()
    {
        if (Keyboard.current == null || !Keyboard.current.tabKey.wasPressedThisFrame) return;
        if (lobbyDoor == null) lobbyDoor = GameObject.Find("LobbyDoor")?.GetComponent<Door>();
        if (lobbyDoor != null) lobbyDoor.Close();
        if (kitchenDoor == null) kitchenDoor = GameObject.Find("KitchenDoor");
        if (kitchenDoor != null) kitchenDoor.SetActive(false);
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
