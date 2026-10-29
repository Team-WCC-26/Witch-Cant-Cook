using System.Collections.Generic;

public sealed class LobbyHudState
{
    private readonly List<string> players = new();
    private readonly HashSet<string> speakingPlayers = new();
    private readonly HashSet<string> readyPlayers = new();
    private double doorCountdownEndsAt;
    public const int DoorCountdownSeconds = 3;
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
        ResetDoorCountdown();
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
        if (readyPlayers.Remove(id)) doorCountdownEndsAt = 0;
        return players.Remove(id);
    }

    public bool SetSpeaking(string id, bool speaking)
    {
        if (string.IsNullOrEmpty(id) || !players.Contains(id)) return false;
        return speaking ? speakingPlayers.Add(id) : speakingPlayers.Remove(id);
    }

    public bool IsSpeaking(string id) => id != null && speakingPlayers.Contains(id);
    public bool SetPlayerOnDoorPlate(string id, bool present, double now)
    {
        if (!IsActive || string.IsNullOrEmpty(id) || !players.Contains(id)) return false;
        bool changed = present ? readyPlayers.Add(id) : readyPlayers.Remove(id);
        if (!changed) return false;
        if (readyPlayers.Count < 2) doorCountdownEndsAt = 0;
        else if (doorCountdownEndsAt == 0) doorCountdownEndsAt = now + DoorCountdownSeconds;
        return true;
    }

    public int GetDoorCountdown(double now)
    {
        if (!IsActive || readyPlayers.Count < 2 || doorCountdownEndsAt == 0) return 0;
        // Keep the final digit until the authoritative door/stage packet arrives; never display 0.
        return System.Math.Clamp((int)System.Math.Ceiling(doorCountdownEndsAt - now), 1, DoorCountdownSeconds);
    }

    private void ResetDoorCountdown()
    {
        readyPlayers.Clear();
        doorCountdownEndsAt = 0;
    }

    public void BeginLobby() => IsActive = IsInRoom;
    public void BeginStage() { IsActive = false; ResetDoorCountdown(); }
}
