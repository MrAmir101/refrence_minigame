using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>One player's running score. Needs to be a struct to live in a NetworkList.</summary>
public struct ScoreEntry : INetworkSerializable, IEquatable<ScoreEntry>
{
    public ulong ClientId;
    public int Points;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ClientId);
        serializer.SerializeValue(ref Points);
    }

    public bool Equals(ScoreEntry other) => ClientId == other.ClientId && Points == other.Points;
}

/// <summary>
/// The harness. Lives in the Hub scene, on the same object as (or next to) NetworkManager.
/// Host-authoritative: only the host loads scenes, spawns avatars and awards points.
///
/// Flow: LaunchMinigame(i) -> load scene additively -> find the MinigameBase in it
///       -> spawn an avatar per player at its spawn points -> minigame runs
///       -> minigame reports placements -> award points -> despawn -> unload scene.
/// </summary>
public class MinigameManager : NetworkBehaviour
{
    public static MinigameManager Instance { get; private set; }

    [Header("Prefab spawned for each player inside a minigame")]
    [SerializeField] private GameObject playerAvatarPrefab;

    [Header("Minigame scene names (must also be in Build Settings)")]
    [SerializeField] private List<string> minigameScenes = new();

    [Header("Points for 1st, 2nd, 3rd... (last value is reused if more players)")]
    [SerializeField] private int[] pointsByPlacement = { 3, 2, 1, 0 };

    public IReadOnlyList<string> MinigameScenes => minigameScenes;

    /// <summary>Replicated scoreboard. Read this on clients for UI.</summary>
    public NetworkList<ScoreEntry> Scores = new();

    /// <summary>True while a minigame scene is loaded and running.</summary>
    public NetworkVariable<bool> MinigameInProgress = new(false);

    private Scene _activeScene;
    private MinigameBase _activeMinigame;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        foreach (var id in NetworkManager.ConnectedClientsIds) EnsureScoreEntry(id);
        NetworkManager.OnClientConnectedCallback += HandleClientConnected;
        NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
    }

    public override void OnNetworkDespawn()
    {
        if (!IsServer) return;
        NetworkManager.OnClientConnectedCallback -= HandleClientConnected;
        NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
    }

    // ------------------------------------------------------------------ launch

    /// <summary>Host only. Start the minigame at this index of the scene list.</summary>
    public void LaunchMinigame(int index)
    {
        if (!IsServer)
        {
            Debug.LogWarning("Only the host can launch a minigame.");
            return;
        }
        if (MinigameInProgress.Value)
        {
            Debug.LogWarning("A minigame is already running.");
            return;
        }
        if (index < 0 || index >= minigameScenes.Count)
        {
            Debug.LogError($"No minigame at index {index}.");
            return;
        }

        MinigameInProgress.Value = true;
        NetworkManager.SceneManager.OnLoadEventCompleted += HandleLoadEventCompleted;

        var status = NetworkManager.SceneManager.LoadScene(minigameScenes[index], LoadSceneMode.Additive);
        if (status != SceneEventProgressStatus.Started)
        {
            Debug.LogError($"Could not load '{minigameScenes[index]}': {status}. Is it in Build Settings?");
            NetworkManager.SceneManager.OnLoadEventCompleted -= HandleLoadEventCompleted;
            MinigameInProgress.Value = false;
        }
    }

    private void HandleLoadEventCompleted(string sceneName, LoadSceneMode mode,
                                          List<ulong> completed, List<ulong> timedOut)
    {
        NetworkManager.SceneManager.OnLoadEventCompleted -= HandleLoadEventCompleted;

        if (timedOut.Count > 0)
            Debug.LogWarning($"{timedOut.Count} client(s) timed out loading '{sceneName}'.");

        _activeScene = SceneManager.GetSceneByName(sceneName);
        _activeMinigame = FindMinigameIn(_activeScene);

        if (_activeMinigame == null)
        {
            Debug.LogError($"Scene '{sceneName}' has no MinigameBase component. Aborting.");
            CleanUpAndUnload();
            return;
        }

        var players = new List<ulong>(NetworkManager.ConnectedClientsIds);
        SpawnAvatars(players);

        _activeMinigame.OnMinigameEnded += HandleMinigameEnded;
        _activeMinigame.HostStart(players.ToArray());
    }

    private static MinigameBase FindMinigameIn(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded) return null;
        foreach (var root in scene.GetRootGameObjects())
        {
            var mg = root.GetComponentInChildren<MinigameBase>(true);
            if (mg != null) return mg;
        }
        return null;
    }

    // ------------------------------------------------------------------ avatars

    private void SpawnAvatars(List<ulong> players)
    {
        if (playerAvatarPrefab == null)
        {
            Debug.LogError("No player avatar prefab assigned on MinigameManager.");
            return;
        }

        var points = _activeMinigame.SpawnPoints;
        for (int i = 0; i < players.Count; i++)
        {
            var pos = (points != null && points.Length > 0)
                ? points[i % points.Length].position
                : Vector3.zero;

            var go = Instantiate(playerAvatarPrefab, pos, Quaternion.identity);
            go.name = $"Avatar_{players[i]}";
            go.GetComponent<NetworkObject>().SpawnAsPlayerObject(players[i]);
        }
    }

    private void DespawnAvatars()
    {
        foreach (var id in new List<ulong>(NetworkManager.ConnectedClientsIds))
        {
            if (!NetworkManager.ConnectedClients.TryGetValue(id, out var client)) continue;
            var po = client.PlayerObject;
            if (po != null && po.IsSpawned) po.Despawn(true);
        }
    }

    // ------------------------------------------------------------------ results

    private void HandleMinigameEnded(ulong[] placements)
    {
        if (_activeMinigame != null) _activeMinigame.OnMinigameEnded -= HandleMinigameEnded;

        for (int i = 0; i < placements.Length; i++)
        {
            int points = pointsByPlacement.Length > 0
                ? pointsByPlacement[Mathf.Min(i, pointsByPlacement.Length - 1)]
                : 0;
            AddPoints(placements[i], points);
        }

        Debug.Log($"Minigame over. Winner: client {(placements.Length > 0 ? placements[0].ToString() : "nobody")}");
        CleanUpAndUnload();
    }

    private void CleanUpAndUnload()
    {
        DespawnAvatars();

        if (_activeScene.IsValid() && _activeScene.isLoaded)
            NetworkManager.SceneManager.UnloadScene(_activeScene);

        _activeMinigame = null;
        _activeScene = default;
        MinigameInProgress.Value = false;
    }

    // ------------------------------------------------------------------ scores

    private void EnsureScoreEntry(ulong clientId)
    {
        for (int i = 0; i < Scores.Count; i++)
            if (Scores[i].ClientId == clientId) return;

        Scores.Add(new ScoreEntry { ClientId = clientId, Points = 0 });
    }

    private void AddPoints(ulong clientId, int points)
    {
        for (int i = 0; i < Scores.Count; i++)
        {
            if (Scores[i].ClientId != clientId) continue;
            Scores[i] = new ScoreEntry { ClientId = clientId, Points = Scores[i].Points + points };
            return;
        }
        Scores.Add(new ScoreEntry { ClientId = clientId, Points = points });
    }

    // ------------------------------------------------------------------ connections

    private void HandleClientConnected(ulong clientId) => EnsureScoreEntry(clientId);

    private void HandleClientDisconnected(ulong clientId)
    {
        // Score entry stays, so a reconnecting player keeps their points.
        if (_activeMinigame != null) _activeMinigame.HostPlayerLeft(clientId);
    }
}
