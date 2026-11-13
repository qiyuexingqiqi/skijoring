using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace SkiRace.Network
{
    // Queries use an ephemeral client port, so several clients can run on one PC.
    public sealed class RaceRoomDiscovery : MonoBehaviour
    {
        public const int DiscoveryPort = 47777;
        private const string Prefix = "SKIRACE1|";
        private UdpClient socket;
        private readonly ConcurrentQueue<Tuple<string, IPEndPoint>> inbox = new ConcurrentQueue<Tuple<string, IPEndPoint>>();
        private bool serving;
        private string code;
        private double deadline;
        private double nextQuery;
        private Action<string> found;
        private Action<string> failed;

        public void Advertise(string roomCode)
        {
            Stop();
            code = roomCode;
            serving = true;
            try { Open(DiscoveryPort); }
            catch (SocketException e) { Debug.LogWarning("房间发现端口不可用：" + e.Message); Stop(); }
        }

        public void Search(string roomCode, Action<string> onFound, Action<string> onFailure)
        {
            Stop();
            code = roomCode;
            found = onFound;
            failed = onFailure;
            deadline = Time.realtimeSinceStartupAsDouble + 8;
            nextQuery = 0;
            try { Open(0); }
            catch (SocketException e) { onFailure("无法搜索房间：" + e.Message); Stop(); }
        }

        private void Open(int port)
        {
            socket = new UdpClient(new IPEndPoint(IPAddress.Any, port));
            socket.EnableBroadcast = true;
            Receive(socket);
        }

        private void Receive(UdpClient listener)
        {
            listener.BeginReceive(ar =>
            {
                try
                {
                    IPEndPoint endpoint = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = listener.EndReceive(ar, ref endpoint);
                    if (data.Length <= 128) inbox.Enqueue(Tuple.Create(Encoding.ASCII.GetString(data), endpoint));
                    if (socket == listener) Receive(listener);
                }
                catch (ObjectDisposedException) { }
                catch (SocketException) { }
            }, null);
        }

        private void Update()
        {
            if (socket == null) return;
            while (inbox.TryDequeue(out var packet))
            {
                if (serving && packet.Item1 == Prefix + "Q|" + code)
                    Send(Prefix + "A|" + code, packet.Item2);
                else if (!serving && packet.Item1 == Prefix + "A|" + code)
                {
                    var callback = found;
                    string address = packet.Item2.Address.ToString();
                    Stop();
                    callback?.Invoke(address);
                    return;
                }
            }
            if (serving) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (now >= deadline)
            {
                var callback = failed;
                Stop();
                callback?.Invoke("未找到房间，请确认房间号、同一局域网及房主已创建房间。也可填写房主 IP 直连。");
            }
            else if (now >= nextQuery)
            {
                nextQuery = now + .5;
                Send(Prefix + "Q|" + code, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
                Send(Prefix + "Q|" + code, new IPEndPoint(IPAddress.Loopback, DiscoveryPort));
            }
        }

        private void Send(string value, IPEndPoint endpoint)
        {
            try { byte[] bytes = Encoding.ASCII.GetBytes(value); socket.Send(bytes, bytes.Length, endpoint); }
            catch (SocketException) { }
        }

        public void Stop()
        {
            var old = socket;
            socket = null;
            old?.Close();
            serving = false;
            found = null;
            failed = null;
            while (inbox.TryDequeue(out _)) { }
        }

        private void OnDestroy() { Stop(); }
    }
}
