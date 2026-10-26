using System.Collections.Generic;
using UnityEngine;

// Scene-owned spawn mapping for the existing S_OpenDoor(Lobby) packet.
public sealed class LobbyReturnSpawnPoints : MonoBehaviour
{
    [Tooltip("로비 참가자 표시 순서와 동일합니다. 0=참가자 1, 1=참가자 2. 양쪽 클라이언트에 같은 배치를 사용하세요.")]
    [SerializeField] private Transform[] spawnPoints = new Transform[2];
    [Tooltip("현재 서버 Stop은 모든 엔티티 ID를 지웁니다. 고정 도구 ID 유지로 서버가 바뀌면 끕니다.")]
    [SerializeField] private bool resetMapRegistration = true;
    public bool ResetMapRegistration => resetMapRegistration;

    public bool TryGetSpawns(IReadOnlyList<string> players, out Dictionary<string, Pose> poses, out string issue)
    {
        poses = new Dictionary<string, Pose>();
        issue = null;
        if (players == null || players.Count == 0)
        {
            issue = "로비 참가자 정보 대기 중";
            return false;
        }
        if (spawnPoints == null || spawnPoints.Length < players.Count)
        {
            issue = "LobbyReturnSpawnPoints: 참가자 수만큼 스폰 위치를 지정하세요.";
            return false;
        }
        for (int i = 0; i < players.Count; i++)
        {
            if (string.IsNullOrEmpty(players[i]) || spawnPoints[i] == null)
            {
                issue = $"LobbyReturnSpawnPoints: 참가자 {i + 1}의 스폰 위치가 없습니다.";
                return false;
            }
            poses[players[i]] = new Pose(spawnPoints[i].position, spawnPoints[i].rotation);
        }
        return true;
    }
}
