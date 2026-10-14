using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Themister_Real
{
    /// <summary>
    /// รับบาร์โค้ดจาก Keyence SR-X100W ผ่าน TCP/IP (Data port, Default 9004)
    /// Scanner ส่งผลการอ่านเป็นข้อความ ASCII ปิดท้ายด้วย CR ทีละบรรทัด
    /// </summary>
    public sealed class KeyenceScanner : IDisposable
    {
        private const int ConnectTimeoutMs = 3000;

        private TcpClient client = null;
        private CancellationTokenSource ctsRead = null;

        // ถูกเรียกจาก Thread เบื้องหลัง (ไม่ใช่ UI Thread)
        public event Action<string> BarcodeReceived;
        public event Action Disconnected;

        public bool IsConnected { get; private set; }

        public async Task ConnectAsync(string ipAddress, int port)
        {
            Disconnect();

            var newClient = new TcpClient();
            try
            {
                Task connectTask = newClient.ConnectAsync(ipAddress, port);
                if (await Task.WhenAny(connectTask, Task.Delay(ConnectTimeoutMs)) != connectTask)
                {
                    throw new TimeoutException($"หมดเวลาเชื่อมต่อ ({ConnectTimeoutMs / 1000} วินาที)");
                }
                await connectTask;

                // ให้ตรวจจับได้เมื่อสาย LAN หลุดหรือ Scanner ดับ
                newClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            }
            catch
            {
                newClient.Close();
                throw;
            }

            client = newClient;
            ctsRead = new CancellationTokenSource();
            IsConnected = true;

            _ = Task.Run(() => ReadLoopAsync(client, ctsRead.Token));
        }

        public void Disconnect()
        {
            IsConnected = false;

            ctsRead?.Cancel();
            ctsRead = null;

            try { client?.Close(); } catch { }
            client = null;
        }

        private async Task ReadLoopAsync(TcpClient tcp, CancellationToken token)
        {
            var buffer = new byte[1024];
            var line = new StringBuilder();

            try
            {
                NetworkStream stream = tcp.GetStream();
                while (!token.IsCancellationRequested)
                {
                    int count = await stream.ReadAsync(buffer, 0, buffer.Length, token);
                    if (count == 0) break; // Scanner ปิดการเชื่อมต่อ

                    foreach (char ch in Encoding.ASCII.GetString(buffer, 0, count))
                    {
                        if (ch == '\r' || ch == '\n')
                        {
                            string text = line.ToString().Trim('\0', ' ');
                            line.Clear();
                            if (text.Length > 0) RaiseBarcodeReceived(text);
                        }
                        else
                        {
                            line.Append(ch);
                        }
                    }
                }
            }
            catch { }

            // หลุดเองโดยไม่ได้สั่ง Disconnect
            if (!token.IsCancellationRequested && ReferenceEquals(tcp, client))
            {
                Disconnect();
                Disconnected?.Invoke();
            }
        }

        // Error จากฝั่งผู้รับ Event ต้องไม่ทำให้ Loop อ่านข้อมูลหยุด
        private void RaiseBarcodeReceived(string text)
        {
            try { BarcodeReceived?.Invoke(text); } catch { }
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
