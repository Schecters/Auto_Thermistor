using System;
using System.Text;
using Keyence.AutoID.SDK;

namespace Themister_Real
{
    /// <summary>
    /// ห่อ Keyence.AutoID.SDK.ReaderAccessor สำหรับรับบาร์โค้ดจาก Keyence Scanner ผ่าน LAN
    /// แยกไว้คลาสนี้คลาสเดียว เพื่อให้ Form2 ยังเปิดได้แม้เครื่องไม่มี Keyence.AutoID.SDK.dll
    /// </summary>
    public sealed class KeyenceScanner : IDisposable
    {
        private ReaderAccessor reader = null;

        // ถูกเรียกจาก Thread ของ SDK (ไม่ใช่ UI Thread)
        public event Action<string> BarcodeReceived;

        public bool IsConnected { get; private set; }
        public string LastError { get; private set; } = "";

        public bool Connect(string ipAddress, int dataPort)
        {
            Disconnect();

            reader = new ReaderAccessor();
            reader.IpAddress = ipAddress;
            reader.DataPort = dataPort;

            IsConnected = reader.Connect(OnDataReceived);
            if (!IsConnected)
            {
                LastError = reader.LastErrorInfo.ToString();
                reader.Dispose();
                reader = null;
            }
            return IsConnected;
        }

        public void Disconnect()
        {
            IsConnected = false;
            if (reader == null) return;

            try
            {
                reader.Disconnect();
                reader.Dispose();
            }
            catch { }
            reader = null;
        }

        private void OnDataReceived(byte[] data)
        {
            if (data == null || data.Length == 0) return;

            string text = Encoding.ASCII.GetString(data).Trim('\r', '\n', '\0', ' ');
            if (text.Length > 0)
            {
                BarcodeReceived?.Invoke(text);
            }
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
