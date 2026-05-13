using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace MdPad.Wpf;

public sealed class LanShareService : IDisposable
{
    public const int DiscoveryPort = 45217;

    private readonly string _localId = Guid.NewGuid().ToString("N");
    private readonly string _machineName = Environment.MachineName;
    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<string, PeerEntry> _peers = [];
    private TcpListener? _listener;
    private UdpClient? _udpBroadcast;
    private UdpClient? _udpReceiver;
    private bool _disposed;
    private int _listenPort;
    private IPAddress _localAddress = IPAddress.Loopback;

    public event Action? PeersChanged;
    public event Action<string, string>? DocumentReceived;

    public IReadOnlyList<NearbyPeer> Peers
    {
        get
        {
            CleanupPeers();
            return _peers.Values
                .OrderBy(peer => peer.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .Select(peer => peer.ToModel())
                .ToList();
        }
    }

    public async Task StartAsync()
    {
        _localAddress = GetPrimaryAddress() ?? IPAddress.Loopback;
        _listener = new TcpListener(IPAddress.Any, 0);
        _listener.Start();
        _listenPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _udpReceiver = new UdpClient(DiscoveryPort) { EnableBroadcast = true };
        _udpBroadcast = new UdpClient { EnableBroadcast = true };
        _ = ListenForTransfersAsync(_cts.Token);
        _ = ListenForDiscoveryAsync(_cts.Token);
        _ = BroadcastLoopAsync(_cts.Token);
        await BroadcastHelloAsync();
    }

    public async Task SendAsync(NearbyPeer peer, string title, string markdown)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Parse(peer.Address), peer.Port);
        var json = JsonSerializer.Serialize(new LanDocumentPayload(title, markdown ?? string.Empty));
        var buffer = Encoding.UTF8.GetBytes(json);
        await client.GetStream().WriteAsync(buffer, 0, buffer.Length);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        _listener?.Stop();
        _udpBroadcast?.Dispose();
        _udpReceiver?.Dispose();
    }

    private async Task BroadcastLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await BroadcastHelloAsync();
                CleanupPeers();
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (SocketException) when (cancellationToken.IsCancellationRequested || _disposed)
        {
        }
    }

    private async Task BroadcastHelloAsync()
    {
        if (_udpBroadcast is null)
        {
            return;
        }

        if (_disposed)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(new LanHelloPayload(_localId, _machineName, _localAddress.ToString(), _listenPort));
        var buffer = Encoding.UTF8.GetBytes(payload);
        await _udpBroadcast.SendAsync(buffer, buffer.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
    }

    private async Task ListenForDiscoveryAsync(CancellationToken cancellationToken)
    {
        if (_udpReceiver is null)
        {
            return;
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await _udpReceiver.ReceiveAsync(cancellationToken);
                var payload = JsonSerializer.Deserialize<LanHelloPayload>(Encoding.UTF8.GetString(result.Buffer));
                if (payload is null || payload.Id == _localId)
                {
                    continue;
                }

                _peers[payload.Id] = new PeerEntry
                {
                    Id = payload.Id,
                    Name = payload.Name,
                    Address = payload.Address,
                    Port = payload.Port,
                    DisplayName = $"{payload.Name} ({payload.Address})",
                    LastSeenAt = DateTime.UtcNow,
                };
                PeersChanged?.Invoke();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (SocketException) when (cancellationToken.IsCancellationRequested || _disposed)
        {
        }
    }

    private async Task ListenForTransfersAsync(CancellationToken cancellationToken)
    {
        if (_listener is null)
        {
            return;
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(cancellationToken);
                _ = Task.Run(async () =>
                {
                    using var ownedClient = client;
                    using var reader = new StreamReader(ownedClient.GetStream(), Encoding.UTF8);
                    var json = await reader.ReadToEndAsync(cancellationToken);
                    var payload = JsonSerializer.Deserialize<LanDocumentPayload>(json);
                    if (payload is not null)
                    {
                        DocumentReceived?.Invoke(payload.Title, payload.Markdown);
                    }
                }, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (SocketException) when (cancellationToken.IsCancellationRequested || _disposed)
        {
        }
    }

    private void CleanupPeers()
    {
        var expired = _peers
            .Where(entry => DateTime.UtcNow - entry.Value.LastSeenAt > TimeSpan.FromSeconds(15))
            .Select(entry => entry.Key)
            .ToList();
        foreach (var id in expired)
        {
            _peers.Remove(id);
        }
    }

    private static IPAddress? GetPrimaryAddress()
    {
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            var address = networkInterface.GetIPProperties().UnicastAddresses
                .FirstOrDefault(candidate => candidate.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(candidate.Address));
            if (address is not null)
            {
                return address.Address;
            }
        }

        return null;
    }

    private sealed class PeerEntry
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public int Port { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public DateTime LastSeenAt { get; set; }

        public NearbyPeer ToModel() => new()
        {
            Id = Id,
            Name = Name,
            Address = Address,
            Port = Port,
            DisplayName = DisplayName,
        };
    }

    private sealed record LanHelloPayload(string Id, string Name, string Address, int Port);
    private sealed record LanDocumentPayload(string Title, string Markdown);
}
