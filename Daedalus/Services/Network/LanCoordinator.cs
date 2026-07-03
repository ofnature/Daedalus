using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Dalamud.Plugin.Services;

namespace Daedalus.Services.Network;

/// <summary>Connection state surfaced to settings/status UI.</summary>
public enum LanStatus
{
    Disabled,
    NoPeers,
    Connected,
    /// <summary>Socket-level failure (port in use etc.) — see <see cref="LanCoordinator.LastError"/>.</summary>
    Error,
}

/// <summary>
/// UDP broadcast transport for cross-machine coordination on the same VLAN (UniFi — same-subnet
/// broadcast needs no router config). Pure .NET <see cref="UdpClient"/> so Windows 10 and 11 behave
/// identically. Fire-and-forget by design: combat signals tolerate occasional loss, and there is no
/// discovery protocol — everyone broadcasts on the same port and finds each other.
///
/// Threading: the receive loop runs on a dedicated background thread and hands raw messages to
/// <see cref="OnMessageReceived"/> ON THAT THREAD — subscribers (CoordinationBus) must queue and
/// drain on the framework thread, never touch game state directly.
/// </summary>
public sealed class LanCoordinator : IDisposable
{
    private readonly IPluginLog _log;
    private readonly string _machineId;
    private readonly int _port;

    private UdpClient? _socket;
    private Thread? _receiveThread;
    private volatile bool _running;

    private long _messagesSent;
    private long _messagesReceived;

    /// <summary>Unique per toon: "Character@World". Own broadcasts are filtered by this.</summary>
    public string SenderId { get; set; } = "";

    public LanStatus Status { get; private set; } = LanStatus.Disabled;
    public string LastError { get; private set; } = "";
    public long MessagesSent => Interlocked.Read(ref _messagesSent);
    public long MessagesReceived => Interlocked.Read(ref _messagesReceived);

    /// <summary>Raised on the RECEIVE THREAD for every non-self message. Queue, don't process.</summary>
    public event Action<LanMessage>? OnMessageReceived;

    public LanCoordinator(IPluginLog log, string machineId, int port)
    {
        _log = log;
        _machineId = machineId;
        _port = port;
    }

    /// <summary>
    /// Binds the socket and starts the receive loop. On failure the coordinator stays down
    /// (Status = Error) and the plugin falls back to local IPC only — never throws.
    /// </summary>
    public bool Start()
    {
        if (_running) return true;

        try
        {
            _socket = new UdpClient();
            // Multiple Daedalus instances per machine share the port (4 clients per box).
            _socket.ExclusiveAddressUse = false;
            _socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _socket.Client.Bind(new IPEndPoint(IPAddress.Any, _port));
            _socket.EnableBroadcast = true;

            _running = true;
            _receiveThread = new Thread(ReceiveLoop)
            {
                IsBackground = true,
                Name = $"Daedalus-LAN-{_port}",
            };
            _receiveThread.Start();

            Status = LanStatus.NoPeers;
            LastError = "";
            _log.Info($"LAN coordinator listening on UDP {_port} (machine {(_machineId.Length > 8 ? _machineId[..8] : _machineId)})");
            return true;
        }
        catch (SocketException ex)
        {
            Status = LanStatus.Error;
            LastError = ex.SocketErrorCode == SocketError.AddressAlreadyInUse
                ? $"Port {_port} in use — change LanPort"
                : ex.Message;
            _log.Error(ex, $"LAN coordinator failed to bind UDP {_port}; falling back to local IPC only");
            CloseSocket();
            return false;
        }
        catch (Exception ex)
        {
            Status = LanStatus.Error;
            LastError = ex.Message;
            _log.Error(ex, "LAN coordinator failed to start; falling back to local IPC only");
            CloseSocket();
            return false;
        }
    }

    /// <summary>Fire-and-forget broadcast. Stamps sender/machine/timestamp. Never throws.</summary>
    public void Send(LanMessage message)
    {
        var socket = _socket;
        if (!_running || socket is null) return;

        try
        {
            message.SenderId = SenderId;
            message.MachineId = _machineId;
            if (message.Timestamp == 0)
                message.Timestamp = DateTime.UtcNow.Ticks;

            var bytes = Encoding.UTF8.GetBytes(message.ToJson());
            socket.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, _port));
            Interlocked.Increment(ref _messagesSent);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "LAN send failed");
        }
    }

    /// <summary>Marks that at least one peer machine is talking to us (drives the status light).</summary>
    public void MarkPeerSeen() => Status = Status == LanStatus.Error ? Status : LanStatus.Connected;

    /// <summary>Drops back to NoPeers when the roster empties (called by the bus).</summary>
    public void MarkNoPeers()
    {
        if (Status == LanStatus.Connected)
            Status = LanStatus.NoPeers;
    }

    private void ReceiveLoop()
    {
        while (_running)
        {
            try
            {
                var socket = _socket;
                if (socket is null) return;

                IPEndPoint remote = new(IPAddress.Any, 0);
                var bytes = socket.Receive(ref remote);
                if (bytes.Length == 0) continue;

                var msg = LanMessage.FromJson(Encoding.UTF8.GetString(bytes));
                if (msg is null) continue;                 // malformed / wrong version — skip, never crash
                if (msg.SenderId == SenderId) continue;    // our own broadcast looping back

                Interlocked.Increment(ref _messagesReceived);
                OnMessageReceived?.Invoke(msg);
            }
            catch (SocketException) when (!_running)
            {
                return; // socket closed during Dispose — clean exit
            }
            catch (Exception ex)
            {
                if (!_running) return;
                // Log and keep the loop alive — a bad datagram or transient socket hiccup must
                // never kill LAN coordination (spec: restart loop, don't crash plugin).
                _log.Warning(ex, "LAN receive loop error — continuing");
                Thread.Sleep(250);
            }
        }
    }

    private void CloseSocket()
    {
        try { _socket?.Close(); } catch { /* shutdown */ }
        _socket = null;
    }

    public void Dispose()
    {
        _running = false;
        CloseSocket(); // unblocks the Receive() call
        _receiveThread?.Join(1000);
        _receiveThread = null;
        Status = LanStatus.Disabled;
    }
}
