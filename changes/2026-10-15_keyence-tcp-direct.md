# 2026-10-15 — Keyence SR-1000W: เชื่อมต่อ TCP ตรง (เลิกใช้ Keyence SDK)

**ไฟล์ที่จะแก้:** `Themister_Real/KeyenceScanner.cs`, `Themister_Real/Form2.cs`, `Themister_Real/Themister_Real.csproj`, `.gitignore`, `README.md`

## เหตุผล
- SR-1000W ส่งผลการอ่านออกทาง **Data port 9004** เป็นข้อความ ASCII ธรรมดา ปิดท้ายด้วย CR
  → ใช้ `TcpClient` ของ .NET รับได้เลย ไม่ต้องใช้ SDK
- Port 9004 **ไม่ใช่ Modbus TCP** (SR-1000W รองรับ TCP/IP, EtherNet/IP, PROFINET — ไม่มี Modbus TCP)
  ถ้าส่งคำสั่ง Modbus ไป port นี้ scanner จะไม่ตอบ จึงใช้ TCP ตรงแทน
- Keyence SDK ต้องใช้ DLL ประกอบอีก 3 ตัว (`Communication.dll`, `VncClientControlCommon*.dll`) และบังคับ x86

## สิ่งที่จะทำ
1. เขียน `KeyenceScanner` ใหม่ด้วย `TcpClient`
   - `ConnectAsync(ip, port)` timeout 3 วินาที
   - Loop อ่านข้อมูล แยกบรรทัดด้วย CR/LF → event `BarcodeReceived`
   - เปิด TCP KeepAlive, ถ้าสายหลุด → event `Disconnected` → ปุ่มกลับเป็น "Connect"
2. ค่าเริ่มต้นของ `txtIPKeynce` / `txtPortKeynce` = `192.168.1.99` / `9004`
3. กด `btConnectKeynce` เพื่อ Connect / Disconnect (เปิดโปรแกรมยัง auto-connect แบบเงียบเหมือนเดิม)
4. ลบ reference `Keyence.AutoID.SDK` และโฟลเดอร์ `lib/` ออกจากโปรเจกต์
5. Release กลับเป็น **AnyCPU** เหมือนเดิม (ไม่ต้องบังคับ x86 แล้ว)

## ไม่เปลี่ยน
- Flow หลังสแกน (`ProcessScannedBarcodeAsync`) และ scanner แบบ USB keyboard
- PLC ยังใช้ Modbus TCP ผ่าน NModbus เหมือนเดิม
