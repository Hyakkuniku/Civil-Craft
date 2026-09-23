using TMPro;
using Fusion;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(TMP_Text))]
public sealed class MultiplayerPingDisplay : MonoBehaviour
{
    private const float RefreshInterval = 0.5f;

    private TMP_Text pingLabel;
    private float nextRefreshTime;

    private void Awake()
    {
        pingLabel = GetComponent<TMP_Text>();
    }

    private void OnEnable()
    {
        nextRefreshTime = 0f;
        if (pingLabel != null) pingLabel.text = "-- ms";
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshTime) return;
        nextRefreshTime = Time.unscaledTime + RefreshInterval;

        FusionConnectionManager fusion = FusionConnectionManager.Instance;
        if (fusion != null && fusion.Runner != null && fusion.Runner.IsRunning)
        {
            NetworkRunner fusionRunner = fusion.Runner;
            PlayerRef peer = fusionRunner.LocalPlayer;
            if (fusion.IsHosting)
            {
                foreach (PlayerRef player in fusionRunner.ActivePlayers)
                {
                    if (player == fusionRunner.LocalPlayer) continue;
                    peer = player;
                    break;
                }
                if (peer == fusionRunner.LocalPlayer)
                {
                    pingLabel.text = "-- ms";
                    return;
                }
            }

            double seconds = fusionRunner.GetPlayerRtt(peer);
            pingLabel.text = seconds > 0d ? $"{Mathf.RoundToInt((float)(seconds * 1000d))} ms" : "-- ms";
            return;
        }

        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || !manager.IsListening || manager.NetworkConfig.NetworkTransport == null)
        {
            pingLabel.text = "-- ms";
            return;
        }

        ulong peerId;
        if (manager.IsHost)
        {
            // The host's own client has no network latency; measure the joined player.
            peerId = manager.LocalClientId;
            foreach (ulong clientId in manager.ConnectedClientsIds)
            {
                if (clientId == manager.LocalClientId) continue;
                peerId = clientId;
                break;
            }

            if (peerId == manager.LocalClientId)
            {
                pingLabel.text = "-- ms";
                return;
            }
        }
        else
        {
            if (!manager.IsConnectedClient)
            {
                pingLabel.text = "-- ms";
                return;
            }
            peerId = NetworkManager.ServerClientId;
        }

        ulong roundTripMs = manager.NetworkConfig.NetworkTransport.GetCurrentRtt(peerId);
        pingLabel.text = roundTripMs > 0 ? $"{roundTripMs} ms" : "-- ms";
    }
}
