# 2026-10-15 — Form2: อ่านบาร์โค้ดจาก Keyence Scanner ผ่าน LAN

**ไฟล์ที่จะแก้:** `Themister_Real/Form2.cs`, `Themister_Real/Themister_Real.csproj`, `.gitignore`, `README.md`
**ไฟล์ใหม่:** `Themister_Real/KeyenceScanner.cs`, `Themister_Real/lib/Keyence.AutoID.SDK.dll` (ไม่ขึ้น git)

## ข้อมูลจาก Keyence.AutoID.SDK.dll (v2.0.0.0)
- ใช้ `ReaderAccessor` — ตั้ง `IpAddress`, `DataPort` แล้ว `Connect(Action<byte[]>)` รับข้อมูลที่อ่านได้ผ่าน callback
- DLL เป็น **x86 เท่านั้น** (Required32Bit) → ต้องเปลี่ยน Release จาก AnyCPU เป็น x86 (Debug เป็น x86 อยู่แล้ว)
- DLL อ้างถึง `Communication.dll`, `VncClientControlCommon.dll`, `VncClientControlCommonLib.dll` จาก SDK ชุดเดียวกัน — ถ้า Connect แล้ว error ว่าหาไฟล์ไม่เจอ ต้องก็อปมาวางคู่กัน

## สิ่งที่จะทำ
1. สร้างคลาส `KeyenceScanner` ห่อ `ReaderAccessor` (Connect / Disconnect / event `BarcodeReceived`)
   แยกไว้ไฟล์เดียว ถ้าเครื่องไม่มี DLL โปรแกรมส่วนอื่นยังเปิดได้
2. ใช้ control ที่มีอยู่แล้วในแท็บ **Connection**: `txtIPKeynce`, `txtPortKeynce` (Data port), `btConnectKeynce`
3. เก็บ IP/Port ใน `keyence_config.cfg` (ค่าเริ่มต้น `192.168.100.100` / `9004` = ค่า default ของ Keyence)
4. เปิดโปรแกรมแล้ว **เชื่อมต่ออัตโนมัติแบบเงียบ** เหมือน PLC, กดปุ่มเพื่อ Connect/Disconnect เอง
5. แยก logic หลังสแกนออกจาก KeyPress เป็น `ProcessScannedBarcodeAsync()` ให้ทั้ง
   Keyboard-wedge scanner (เดิม) และ Keyence ใช้ร่วมกัน → ใส่ SN, เช็ค Routing (โหมด MES), Start PLC
6. ข้อความ `ERROR` จาก Keyence (อ่านไม่ออก) → แสดง "Scan Error" ไม่ Start
7. ปิดโปรแกรม → Disconnect scanner

## ไม่เปลี่ยน
- การสแกนแบบ keyboard (USB scanner) ยังใช้ได้เหมือนเดิม
- ไม่ส่งคำสั่ง trigger (LON/LOFF) — ใช้โหมด trigger ที่ตั้งไว้ในตัว scanner
