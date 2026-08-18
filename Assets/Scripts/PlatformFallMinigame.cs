using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// The reference minigame. Deliberately the dumbest thing that works:
/// everyone stands on a platform, whoever falls off is out, last one standing wins.
///
/// This exists to prove the harness works and to show the team the shape of a
/// minigame. Copy this file when you start yours.
/// </summary>
public class PlatformFallMinigame : MinigameBase
{
    [Header("Below this Y, a player is eliminated")]
    [SerializeField] private float killHeight = -5f;

    private readonly List<ulong> _alive = new();
    private readonly List<ulong> _out = new();   // first eliminated first
    private bool _running;

    protected override void StartMinigame(ulong[] players)
    {
        _alive.Clear();
        _out.Clear();
        _alive.AddRange(players);
        _running = _alive.Count > 0;

        Debug.Log($"PlatformFall started with {_alive.Count} players.");
    }

    protected override void OnPlayerLeft(ulong clientId)
    {
        if (_alive.Remove(clientId)) _out.Add(clientId);
        CheckForWinner();
    }

    protected override void Update()
    {
        base.Update();                       // keeps the time-limit safety net alive
        if (!IsServer || !_running) return;

        for (int i = _alive.Count - 1; i >= 0; i--)
        {
            var id = _alive[i];

            if (!NetworkManager.ConnectedClients.TryGetValue(id, out var client) ||
                client.PlayerObject == null)
            {
                Eliminate(id);
                continue;
            }

            if (client.PlayerObject.transform.position.y < killHeight)
                Eliminate(id);
        }

        CheckForWinner();
    }

    private void Eliminate(ulong clientId)
    {
        if (!_alive.Remove(clientId)) return;
        _out.Add(clientId);
        Debug.Log($"Client {clientId} fell off.");
    }

    private void CheckForWinner()
    {
        if (!_running || _alive.Count > 1) return;

        _running = false;

        // 1st place = whoever is still standing, then the eliminated in reverse
        // order (the one who survived longest comes next).
        var placements = new List<ulong>(_alive);
        for (int i = _out.Count - 1; i >= 0; i--) placements.Add(_out[i]);

        EndMinigame(placements.ToArray());
    }

    protected override ulong[] GetPlacementsOnTimeout()
    {
        var placements = new List<ulong>(_alive);
        for (int i = _out.Count - 1; i >= 0; i--) placements.Add(_out[i]);
        return placements.ToArray();
    }
}
