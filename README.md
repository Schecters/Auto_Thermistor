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
