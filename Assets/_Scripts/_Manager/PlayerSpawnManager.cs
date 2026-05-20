using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

public sealed class PlayerSpawnManager : Singleton<PlayerSpawnManager>
{
    [Header("Local Player")]
    [SerializeField] private string myID = null;

    [Header("Spawn")]
    [SerializeField] private PlayerBrain playerPrefab = null;
    [SerializeField] private Transform spawnRoot = null;

    [Header("Camera")]
    [SerializeField] private Camera mainCamera = null;
    [SerializeField] private CinemachineCamera virtualCamera = null;

    private readonly Dictionary<string, PlayerBrain> players = new();

    public string MyID
    {
        get => myID;
        set => myID = value;
    }

    public bool IsMine(string playerId)
    {
        return !string.IsNullOrEmpty(playerId) && playerId == myID;
    }

    public PlayerBrain SpawnPlayer(string playerId)
    {
        if (string.IsNullOrEmpty(playerId)) return null;

        if (players.TryGetValue(playerId, out PlayerBrain existing))
            return existing;

        Vector3 position = Vector3.zero;
        Quaternion rotation = Quaternion.identity;

        PlayerBrain player = Instantiate(playerPrefab, spawnRoot.position, spawnRoot.rotation, spawnRoot);
        player.Initialize(playerId);
        if (IsMine(playerId)) BindCamera(player); 

        return player;
    }

    public void RegisterPlayer(PlayerBrain player)
    {
        if (player == null || string.IsNullOrEmpty(player.PlayerId))
        {
            return;
        }

        players[player.PlayerId] = player;
    }

    public void UnregisterPlayer(PlayerBrain player)
    {
        if (player == null || string.IsNullOrEmpty(player.PlayerId))
        {
            return;
        }

        if (players.TryGetValue(player.PlayerId, out PlayerBrain registeredPlayer) &&
            registeredPlayer == player)
        {
            players.Remove(player.PlayerId);
        }
    }

    public bool TryGetPlayer(string playerId, out PlayerBrain player)
    {
        return players.TryGetValue(playerId, out player);
    }

    public bool ContainsPlayer(string playerId)
    {
        return players.ContainsKey(playerId);
    }

    private void BindCamera(PlayerBrain player)
    {
        player.BindCamera(mainCamera, virtualCamera);
    }
}