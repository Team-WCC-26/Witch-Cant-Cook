using System;
using System.Collections.Generic;

public sealed class LobbyHudState
{
    private readonly List<string> players = new();
    private readonly HashSet<string> speakingPlayers = new();
    private double enteredAt;
    public string RoomCode { get; private set; } = "";
    public string LocalPlayerId { get; private set; } = "";
    public bool IsInRoom => !string.IsNullOrEmpty(RoomCode);
    public bool IsActive { get; private set; }
    public float DurationSeconds { get; private set; } = 30f;
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

    public void Enter(string code, string localId, string otherId, double now)
    {
        Reset();
        RoomCode = code ?? "";
        LocalPlayerId = localId ?? "";
        // In the current two-player protocol, the existing member is the earlier participant.
        AddPlayer(otherId);
        AddPlayer(localId);
        BeginLobby(now);
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
    public void BeginLobby(double now) { enteredAt = now; IsActive = IsInRoom; }
    public void BeginStage() => IsActive = false;
    public void SetDuration(float seconds) => DurationSeconds = Math.Max(0f, seconds);
    // For a future server/voice integration: apply a received countdown without restarting it.
    public void SetRemainingSeconds(float remaining, double now) => enteredAt = now - DurationSeconds + Math.Max(0f, remaining);
    public int RemainingSeconds(double now) => (int)Math.Ceiling(Math.Max(0d, enteredAt + DurationSeconds - now));
}
