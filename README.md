# Auto_Thermistor

โปรแกรม Themister_Real (WinForms .NET Framework) สำหรับทดสอบ Thermistor ผ่าน PLC (Modbus TCP), DMM Picotest M352xA (USB) และ MES

## โครงสร้าง
- `Themister_Real.slnx` — Solution (เปิดด้วย Visual Studio)
- `Themister_Real/` — Source code (`Form1` = Login, `Form2` = หน้าทดสอบหลัก)
- `changes/` — บันทึกแผนการแก้ไข (เขียนก่อนแก้โค้ดทุกครั้ง)

## Build
1. เปิด `Themister_Real.slnx` ใน Visual Studio
2. Restore NuGet packages (NModbus, System.Text.Json ฯลฯ)
3. ไฟล์ `.pfx` ไม่ได้อยู่ใน repo — ถ้า build ไม่ผ่านเรื่อง signing ให้สร้าง test certificate ใหม่ใน Project Properties > Signing

## Change log
- [2026-10-15 — Form2: ปรับหน้าตา UI ให้สวยขึ้น](changes/2026-10-15_form2-ui-theme.md)
- [2026-10-15 — Form2: อ่านบาร์โค้ดจาก Keyence Scanner ผ่าน LAN](changes/2026-10-15_keyence-scanner.md)
- [2026-10-15 — Keyence SR-X100W: เชื่อมต่อ TCP ตรง (เลิกใช้ SDK)](changes/2026-10-15_keyence-tcp-direct.md)
- [2026-10-15 — ป้าย PASS/FAIL ตัดคำ + แก้จุดเสี่ยงจากการตรวจโค้ด](changes/2026-10-15_passfail-label-and-fixes.md)
- [2026-10-16 — Thermistor: เลือกอุปกรณ์ USB จาก Dropdown + เลิก Popup แจ้งหลุด](changes/2026-10-16_thermistor-usb-dropdown.md)
- [2026-10-16 — รองรับ Chroma 12061 ผ่าน USB (ยังใช้กับ Picotest ได้)](changes/2026-10-16_chroma-12061-support.md)
- [2026-10-26 — เปิด Form2 ทันที, ล็อก Line/Station/MO/MES, ตรวจสิทธิ์พนักงานผ่าน API](changes/2026-10-26_open-form2-direct-operator-check.md)
- [2026-10-27 — โหมด Manual / Auto: เลือกแหล่งสแกน + รอ M30 ก่อนส่ง MES](changes/2026-10-27_manual-auto-scanner-mode.md)
- [2026-10-27 — โหมด Auto: รอ M3031 (แทน M30) ก่อนส่ง API แล้วสั่ง M3030](changes/2026-10-27_auto-wait-m3031.md)
- [2026-10-27 — Routing ไม่ผ่าน → สั่ง M3030 ปล่อยชิ้นงาน](changes/2026-10-27_routing-fail-release-m3030.md)
- [2026-10-27 — แก้บั๊ก: ตรวจพนักงานผ่านแล้ว สแกนชิ้นงานด้วย Scanner USB ไม่ได้](changes/2026-10-27_empid-focus-bug.md)
- [2026-10-27 — แก้บั๊ก: Station ที่ไม่มีข้อสอบบังคับ ตรวจพนักงานแล้วขึ้น FAIL](changes/2026-10-27_operator-no-exam-pass.md)
- [2026-10-27 — โหมด Manual: รับบาร์โค้ดจาก Keyence ได้ด้วย](changes/2026-10-27_manual-allow-keyence.md)
- [2026-10-29 — สแกนแล้วรอ M3031 ก่อน Check Routing + สั่ง M3030 ทุกกรณีที่ไม่ผ่าน](changes/2026-10-29_m3031-triggers-routing-check.md)
- [2026-10-29 — เลิกยิง API routing-check ก่อนทดสอบ](changes/2026-10-29_remove-routing-check.md)
- [2026-10-29 — เขียน Log สิ่งที่ส่งไป MES แยกไฟล์ (mes_log.txt)](changes/2026-10-29_mes-send-log.md)
