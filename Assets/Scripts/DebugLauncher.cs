using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Throwaway on-screen UI so you don't have to build a single Canvas today.
/// Host/Client buttons, live scoreboard, and one launch button per minigame.
/// This gets deleted the day the real board scene exists.
///
/// Put it on any GameObject in the Hub scene.
/// </summary>
public class DebugLauncher : MonoBehaviour
{
    private void OnGUI()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        GUILayout.BeginArea(new Rect(10, 10, 280, 420), GUI.skin.box);

        if (!nm.IsClient && !nm.IsServer)
        {
            GUILayout.Label("Not connected");
            if (GUILayout.Button("Start Host")) nm.StartHost();
            if (GUILayout.Button("Start Client")) nm.StartClient();
        }
        else
        {
            GUILayout.Label(nm.IsHost ? "HOST" : "CLIENT");
            GUILayout.Label($"My client id: {nm.LocalClientId}");
            GUILayout.Space(6);

            var mm = MinigameManager.Instance;
            if (mm == null || !mm.IsSpawned)
            {
                GUILayout.Label("Waiting for MinigameManager...");
            }
            else
            {
                GUILayout.Label("--- Scores ---");
                foreach (var entry in mm.Scores)
                    GUILayout.Label($"Player {entry.ClientId}: {entry.Points}");

                GUILayout.Space(6);

                if (nm.IsServer)
                {
                    if (mm.MinigameInProgress.Value)
                    {
                        GUILayout.Label("Minigame running...");
                    }
                    else
                    {
                        GUILayout.Label("--- Launch ---");
                        for (int i = 0; i < mm.MinigameScenes.Count; i++)
                            if (GUILayout.Button(mm.MinigameScenes[i]))
                                mm.LaunchMinigame(i);
                    }
                }
            }

            GUILayout.Space(6);
            if (GUILayout.Button("Disconnect")) nm.Shutdown();
        }

        GUILayout.EndArea();
    }
}
