using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Every minigame inherits from this. This is THE contract between the harness
/// and the five minigames the team is going to write.
///
/// Rules for minigame authors:
///   1. The host decides everything. Clients only send input via ServerRpc.
///   2. Call EndMinigame(placements) exactly once when the game is over,
///      ordered 1st place -> last place.
///   3. Do not touch MinigameManager. If you need something it doesn't give you,
///      ask, and it gets added here.
/// </summary>
public abstract class MinigameBase : NetworkBehaviour
{
    [Header("Where players appear when this minigame starts")]
    [SerializeField] private Transform[] spawnPoints;

    [Header("Safety net: minigame is force-ended after this many seconds (0 = never)")]
    [SerializeField] private float timeLimitSeconds = 120f;

    public Transform[] SpawnPoints => spawnPoints;

    /// <summary>Host only. Fires once, ordered 1st place -> last place.</summary>
    public event Action<ulong[]> OnMinigameEnded;

    /// <summary>Client ids taking part. Valid on the host after StartMinigame.</summary>
    protected ulong[] Players { get; private set; } = Array.Empty<ulong>();

    private bool _ended;
    private bool _started;
    private float _elapsed;

    // ---- Called by MinigameManager. Minigame authors ignore these two. ----

    public void HostStart(ulong[] players)
    {
        if (!IsServer) return;
        Players = players;
        _ended = false;
        _started = true;
        _elapsed = 0f;
        StartMinigame(players);
    }

    public void HostPlayerLeft(ulong clientId)
    {
        if (!IsServer || _ended) return;
        OnPlayerLeft(clientId);
    }

    // ---- What minigame authors implement ----

    /// <summary>
    /// Host only. Called once, after every player's avatar has been spawned
    /// at your spawn points. Set up your round here.
    /// </summary>
    protected abstract void StartMinigame(ulong[] players);

    /// <summary>
    /// Host only. A player disconnected mid-round. The minigame keeps running.
    /// Default does nothing; override if you track a player list.
    /// </summary>
    protected virtual void OnPlayerLeft(ulong clientId) { }

    /// <summary>
    /// Host only. Called if the time limit runs out and you haven't ended yet.
    /// Return the placements you'd give right now. Default = current player order.
    /// </summary>
    protected virtual ulong[] GetPlacementsOnTimeout() => Players;

    /// <summary>
    /// Host only. Call this once when the round is decided.
    /// placements[0] is the winner, placements[last] came last.
    /// </summary>
    protected void EndMinigame(ulong[] placements)
    {
        if (!IsServer || _ended) return;
        _ended = true;
        _started = false;
        OnMinigameEnded?.Invoke(placements ?? Array.Empty<ulong>());
    }

    protected virtual void Update()
    {
        if (!IsServer || !_started || _ended) return;
        if (timeLimitSeconds <= 0f) return;

        _elapsed += Time.deltaTime;
        if (_elapsed >= timeLimitSeconds)
        {
            Debug.LogWarning($"[{name}] hit its time limit, ending on timeout.");
            EndMinigame(GetPlacementsOnTimeout());
        }
    }

    private void OnValidate()
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
            Debug.LogWarning($"[{name}] has no spawn points assigned.", this);
    }
}
