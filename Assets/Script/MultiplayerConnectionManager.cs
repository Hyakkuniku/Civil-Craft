using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using UnityEngine;

/// <summary>
/// Initializes the Unity services identity used by Relay. PlayFab remains the
/// game's account and save identity.
/// </summary>
public sealed class MultiplayerConnectionManager : MonoBehaviour
{
    private Task authenticationTask;
    private int hostOperationVersion;
    private int clientOperationVersion;
    private TaskCompletionSource<bool> clientConnectionCompletion;

    public string HostJoinCode { get; private set; }

    public bool IsHosting
    {
        get
        {
            NetworkManager manager = GetComponent<NetworkManager>();
            return manager != null && manager.IsListening && manager.IsHost;
        }
    }

    public bool IsClientConnected
    {
        get
        {
            NetworkManager manager = GetComponent<NetworkManager>();
            return manager != null && manager.IsConnectedClient && !manager.IsHost;
        }
    }

    public bool IsAuthenticated =>
        UnityServices.State == ServicesInitializationState.Initialized &&
        AuthenticationService.Instance.IsSignedIn;

    private async void Start()
    {
        try
        {
            await EnsureAuthenticatedAsync();
            if (this != null)
                Debug.Log("[Multiplayer] Unity Authentication sign-in succeeded.", this);
        }
        catch (Exception exception)
        {
            if (this != null)
                Debug.LogError($"[Multiplayer] Unity Authentication sign-in failed: {exception}", this);
        }
    }

    public Task EnsureAuthenticatedAsync()
    {
        if (authenticationTask == null || authenticationTask.IsFaulted || authenticationTask.IsCanceled)
            authenticationTask = AuthenticateAsync();

        return authenticationTask;
    }

    /// <summary>Creates one Relay slot for the second player and starts the local host.</summary>
    public async Task<string> StartHostWithRelayAsync()
    {
        if (IsHosting) return HostJoinCode;

        NetworkManager manager = GetComponent<NetworkManager>();
        UnityTransport transport = GetComponent<UnityTransport>();
        if (manager == null || transport == null || manager.NetworkConfig.NetworkTransport != transport)
            throw new InvalidOperationException("The multiplayer NetworkManager needs its UnityTransport assigned.");
        if (manager.IsListening)
            throw new InvalidOperationException("A network session is already running.");

        int operationVersion = ++hostOperationVersion;
        await EnsureAuthenticatedAsync();
        ThrowIfHostingCancelled(operationVersion);

        var allocation = await RelayService.Instance.CreateAllocationAsync(1);
        ThrowIfHostingCancelled(operationVersion);

        string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
        ThrowIfHostingCancelled(operationVersion);

        transport.SetRelayServerData(new RelayServerData(allocation, "dtls"));
        if (!manager.StartHost())
        {
            manager.Shutdown();
            throw new InvalidOperationException("Netcode could not start the Relay host.");
        }

        HostJoinCode = joinCode;
        return joinCode;
    }

    public void StopHosting()
    {
        ++hostOperationVersion;
        HostJoinCode = null;

        NetworkManager manager = GetComponent<NetworkManager>();
        if (manager != null && manager.IsListening && manager.IsHost)
            manager.Shutdown();
    }

    /// <summary>Resolves a host's Relay code and waits for Netcode scene synchronization.</summary>
    public async Task JoinWithRelayAsync(string joinCode)
    {
        if (string.IsNullOrWhiteSpace(joinCode))
            throw new ArgumentException("Enter a Relay join code.", nameof(joinCode));

        NetworkManager manager = GetComponent<NetworkManager>();
        UnityTransport transport = GetComponent<UnityTransport>();
        if (manager == null || transport == null || manager.NetworkConfig.NetworkTransport != transport)
            throw new InvalidOperationException("The multiplayer NetworkManager needs its UnityTransport assigned.");
        if (manager.IsListening)
            throw new InvalidOperationException("A network session is already running.");

        int operationVersion = ++clientOperationVersion;
        await EnsureAuthenticatedAsync();
        ThrowIfJoiningCancelled(operationVersion);

        var allocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
        ThrowIfJoiningCancelled(operationVersion);

        transport.SetRelayServerData(new RelayServerData(allocation, "dtls"));

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        clientConnectionCompletion = completion;

        void HandleConnected(ulong clientId)
        {
            if (clientId == manager.LocalClientId)
                completion.TrySetResult(true);
        }

        void HandleDisconnected(ulong clientId)
        {
            if (clientId == manager.LocalClientId)
                completion.TrySetException(new InvalidOperationException("The host disconnected or refused the connection."));
        }

        manager.OnClientConnectedCallback += HandleConnected;
        manager.OnClientDisconnectCallback += HandleDisconnected;
        try
        {
            if (!manager.StartClient())
                throw new InvalidOperationException("Netcode could not start the Relay client.");

            int timeoutSeconds = Math.Max(30, manager.NetworkConfig.LoadSceneTimeOut + 5);
            Task finished = await Task.WhenAny(completion.Task,
                Task.Delay(TimeSpan.FromSeconds(timeoutSeconds)));
            if (finished != completion.Task)
                throw new TimeoutException("The host did not accept the connection in time.");

            await completion.Task;
            ThrowIfJoiningCancelled(operationVersion);
        }
        catch
        {
            if (operationVersion == clientOperationVersion && manager != null && manager.IsClient)
                manager.Shutdown();
            throw;
        }
        finally
        {
            if (manager != null)
            {
                manager.OnClientConnectedCallback -= HandleConnected;
                manager.OnClientDisconnectCallback -= HandleDisconnected;
            }
            if (ReferenceEquals(clientConnectionCompletion, completion))
                clientConnectionCompletion = null;
        }
    }

    public void StopJoining()
    {
        ++clientOperationVersion;
        clientConnectionCompletion?.TrySetCanceled();

        NetworkManager manager = GetComponent<NetworkManager>();
        if (manager != null && manager.IsClient && !manager.IsHost)
            manager.Shutdown();
    }

    private void ThrowIfHostingCancelled(int operationVersion)
    {
        if (this == null || operationVersion != hostOperationVersion)
            throw new OperationCanceledException("Relay host creation was cancelled.");
    }

    private void ThrowIfJoiningCancelled(int operationVersion)
    {
        if (this == null || operationVersion != clientOperationVersion)
            throw new OperationCanceledException("Relay join was cancelled.");
    }

    private static async Task AuthenticateAsync()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
            await UnityServices.InitializeAsync();

        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }
}
