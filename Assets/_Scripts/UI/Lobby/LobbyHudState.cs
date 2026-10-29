using System.Collections.Generic;

public sealed class LobbyHudState
{
    private readonly List<string> players = new();
    private readonly HashSet<string> speakingPlayers = new();
    public string RoomCode { get; private set; } = "";
    public string LocalPlayerId { get; private set; } = "";
    public bool IsInRoom => !string.IsNullOrEmpty(RoomCode);
    public bool IsActive { get; private set; }
    public IReadOnlyList<string> Players { get; }

    public LobbyHudState() => Players = players.AsReadOnly();

    public void Reset()
    {
        RoomCode = "";
        LocalPlayerId = "";
        IsActive = false;
        players.Clear();
        speakingPlayers.Clear();
    }

    public void Enter(string code, string localId, string otherId)
    {
        Reset();
        RoomCode = code ?? "";
        LocalPlayerId = localId ?? "";
        // In the current two-player protocol, the existing member is the earlier participant.
        AddPlayer(otherId);
        AddPlayer(localId);
        BeginLobby();
    }

    public bool AddPlayer(string id)
    {
        if (string.IsNullOrEmpty(id) || players.Contains(id)) return false;
        players.Add(id);
        return true;
    }

    public bool RemovePlayer(string id)
    {
        if (id == null) return false;
        speakingPlayers.Remove(id);
        return players.Remove(id);
    }

    public bool SetSpeaking(string id, bool speaking)
    {
        if (string.IsNullOrEmpty(id) || !players.Contains(id)) return false;
        return speaking ? speakingPlayers.Add(id) : speakingPlayers.Remove(id);
    }

    public bool IsSpeaking(string id) => id != null && speakingPlayers.Contains(id);
    public void BeginLobby() => IsActive = IsInRoom;
    public void BeginStage() => IsActive = false;
}
