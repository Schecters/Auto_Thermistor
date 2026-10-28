# CLAUDE.md

โปรแกรม Themister_Real (WinForms .NET Framework) ทดสอบ Thermistor ผ่าน PLC (Modbus TCP / NModbus), DMM USB (Picotest M352xA / Chroma 12061), Barcode Scanner (USB Keyboard Wedge + Keyence SR-X100W ผ่าน LAN) และส่งผลไป MES

## โครงสร้าง / กติกา
- `Themister_Real/Form2.cs` — หน้าทดสอบหลัก (เกือบทุก Logic อยู่ที่นี่), `Form1.cs` — Login (ME/ADMIN) เปิดเป็น Popup จาก `btSetting`, `KeyenceScanner.cs` — TCP Client ของ Keyence
- เขียนแผนใน `changes/YYYY-MM-DD_<ชื่อ>.md` **ก่อน** แก้โค้ดทุกครั้ง แล้วเพิ่มลิงก์ใน Change log ของ `README.md`
- เลี่ยงการแก้ `Form2.Designer.cs` (Visual Studio Designer เขียนทับ) — ปรับสี/สไตล์ใน `ApplyTheme()` ของ `Form2.cs` แทน แก้ Designer เฉพาะตอนเพิ่ม Control ใหม่
- แก้เสร็จ (Build ผ่าน) ทุกครั้ง → `git commit` แล้ว `git push` ขึ้น remote ทันที ไม่ต้องรอให้สั่ง
- `Form2.cs` ใช้ Line ending แบบ CRLF
- Build: `MSBuild Themister_Real\Themister_Real.csproj /p:Configuration=Debug` (ไฟล์ `.pfx` ไม่อยู่ใน repo — ถ้า Signing พังให้สร้าง Test Certificate ใหม่)

## Flow ชิ้นงานปัจจุบัน (ตั้งแต่ 2026-10-29)
```
สแกน (USB / Keyence) → เก็บ SN ไว้ (_pendingBarcode)       PLC: M3031 = 1 (ชิ้นงานพร้อมที่ Station)
                   └──────────── มีครบทั้งสองอย่าง (อะไรมาก่อนก็ได้) ───────────┘
  → SN = "ERROR"? → FAIL + M3030
  → (โหมด MES) Check Routing ไม่ผ่าน / MES ล่ม → Popup + M3030
  → ทดสอบตาม Step → PASS: ส่ง routing-update + M3030 / FAIL: Popup (OK = ส่ง MES, Cancel = ไม่ส่ง) + M3030
  → PLC ดีดชิ้นงานออก → M3031 = 0 → รับชิ้นถัดไป (1 รอบ M3031 ON = 1 ชิ้น)
```
- **M3031** (Coil 3031, `PlcPartReadyAddress`) = PLC แจ้งชิ้นงานพร้อม → สัญญาณเริ่ม Check Routing
- **M3030** (Coil 3030, `PlcReleaseAddress`) = สั่ง PLC ดีดชิ้นงานออกจาก Station — ต้องส่งทุกครั้งที่ไม่ผ่าน/ผิดพลาด ยกเว้น: ผู้ใช้กด Stop, ยังไม่ผ่านการตรวจพนักงาน, PLC ไม่ได้เชื่อมต่อ
- โหมด Manual / Auto (`cbManualAuto`) ต่างกันแค่แหล่งสแกน: Manual = USB + Keyence, Auto = Keyence เท่านั้น
- ปุ่ม Start บนหน้าจอ = ทดสอบ SN ใน `txtBarcode` ทันที (ไม่รอ M3031, ไม่ Check Routing)
- ต้องผ่านการตรวจพนักงาน (`txtEMPID` + Enter → API `operator-data`) ก่อนจึงสแกน/Start ได้

## ปัญหาที่แก้แล้ว
รายละเอียดแต่ละเรื่องอยู่ใน `changes/`

| วันที่ | ปัญหา | สาเหตุ | แก้อย่างไร |
|---|---|---|---|
| 10-15 | ป้าย PASS/FAIL แสดงแค่ "REA" แทน "READY" | ธีมใหม่ปิด AutoSize + ฟอนต์ Segoe UI ตัวหนากว้างกว่ากล่อง | คำนวณขนาดฟอนต์ให้พอดีกล่องทุกครั้งที่ข้อความ/ขนาดหน้าต่างเปลี่ยน (`FitPassFailFont`) |
| 10-15 | ป้ายค้างที่ "TEST" เมื่อ Sequence ไม่ครบ / กด Stop แล้วขึ้น "Execution Error" | ไม่ Reset ป้ายใน `finally` / ดักแค่ `TaskCanceledException` | Reset เป็น READY เมื่อไม่มีผล, ดัก `OperationCanceledException` |
| 10-15 | JSON พังถ้า SN/รหัสผ่านมี `"` หรือ `\` | ต่อ JSON ด้วย string | ใช้ `JsonSerializer` ทั้ง Login และ Update Route |
| 10-15 | Keyence ต้องใช้ SDK + DLL 3 ตัว + บังคับ x86 | — | เลิกใช้ SDK อ่าน TCP Data Port 9004 ตรง (ASCII ปิดท้าย CR) ด้วย `TcpClient`, KeepAlive, Event `Disconnected` (Port นี้ไม่ใช่ Modbus) |
| 10-15 | Keyence ส่ง "ERROR" แล้วไลน์ค้างรอ PLC | แค่แสดง "Scan Error" | ทำเหมือน Scanner USB: FAIL + M3030 |
| 10-16 | Chroma 12061 Connect ได้แต่วัดค่าไม่ได้ | เลือก Interrupt-IN แทน Bulk-IN, Timeout 1.5 s สั้นไป, `SYST:REM` ทำให้ Chroma error, ไม่ส่ง USB488 `REN_CONTROL` | เลือก Bulk Endpoint ตัวแรก, Timeout 3 s, ส่ง `REN_CONTROL` + `*IDN?`, ส่ง `SYST:REM` เฉพาะ Picotest, เพิ่มปุ่ม Test |
| 10-16 | เลือก DMM เองไม่ได้ + Popup "USB DMM Disconnected" วนซ้ำ | หาอุปกรณ์จาก VID/PID ตายตัว | Dropdown `cbThermistorUsb` จาก SetupAPI, จำค่าใน `thermistor_config.cfg`, ลบ Popup ใช้ไฟสถานะแทน, Monitor ต่อใหม่เองแบบเงียบ (ยกเว้นกด Disconnect) |
| 10-20 | ผู้ใช้ทั่วไปเปลี่ยน Popup / No Popup ได้ง่าย | `cbPopup` แสดงตลอด | ซ่อนไว้ คลิก "Modbus TCP" (`label15`) 3 ครั้งเพื่อแสดง (นับด้วย `MouseDown`) |
| 10-26 | ต้อง Login ME/ADMIN ก่อนเข้าหน้าทดสอบ, ไม่ได้ตรวจสิทธิ์พนักงาน | — | เปิด Form2 ทันที, ล็อก Line/Station/MO/MES (ปลดด้วย `btSetting` + Login), ตรวจพนักงานผ่าน API `operator-data` (Station THM) |
| 10-27 | ตรวจพนักงานผ่านแล้ว สแกนด้วย USB ไม่ได้ บาร์โค้ดไปต่อท้ายใน `txtEMPID` | Focus ค้างใน `txtEMPID` ตัวดักบาร์โค้ดจึงข้าม | ตรวจผ่านแล้วย้าย Focus ไป `txtBarcode` / `ActiveControl = null` |
| 10-27 | Station ที่ไม่มีข้อสอบบังคับ ตรวจพนักงานแล้วขึ้น FAIL | API ไม่ส่ง `summary` มา | ไม่มี `summary` → ตัดสินจาก `status == "pass"` |
| 10-27 | Routing ไม่ผ่าน ชิ้นงานค้างบนเครื่อง | ขึ้น Popup แล้วจบ ไม่สั่ง M3030 | Routing ไม่ผ่าน / MES ล่ม / ยังไม่เลือก Line-Station-MO → M3030 |
| 10-27 | รับบาร์โค้ดทั้งสองแหล่งพร้อมกัน, ส่ง MES ทันทีไม่รอ PLC | — | เพิ่ม `cbManualAuto` เลือกแหล่งสแกน; Auto รอ M30 → เปลี่ยนเป็น M3031 ก่อนส่ง MES; Manual รับ Keyence ได้ด้วย *(ส่วน "รอ M3031 ก่อนส่ง MES" ถูกแทนที่ในรอบ 10-29)* |
| 10-29 | สแกนแล้ว Check Routing ทันที ไม่รอชิ้นงานพร้อม | Flow เริ่มจากการสแกน | สแกนแค่เก็บ SN → รอ M3031 = 1 แล้วค่อย Check Routing → ทดสอบ (ทั้ง Manual/Auto), เลิกรอ M3031 ก่อนส่ง MES |
| 10-29 | ไม่ได้เลือก Step / Recipe ว่าง / Error ระหว่างทดสอบ ชิ้นงานค้าง | Popup แล้ว return ไม่สั่ง M3030 | เพิ่ม M3030 ทุกจุดนี้ (แยก `btPLCStart_Click` → `RunSequenceAsync`) |

## ยังไม่ได้ทำ / ข้อควรรู้
- PLC ต้องเคลียร์ M3031 เป็น 0 หลังดีดชิ้นงานออก ไม่งั้นโปรแกรมจะไม่รับชิ้นถัดไป
- Flow M3031 → M3030 แบบใหม่ (10-29) Build ผ่านแล้ว แต่ยังไม่ได้ทดสอบกับ PLC จริง
- ข้อเสนอค้างจาก 10-15:
  - ใช้ `HttpClient` ตัวเดียวร่วมกัน
  - ใส่ Log ใน `catch { }` เงียบหลายจุด
  - Keyence หลุดแล้วให้ต่อใหม่เอง
