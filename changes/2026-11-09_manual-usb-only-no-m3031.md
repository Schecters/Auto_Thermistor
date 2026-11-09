# 2026-11-09 — โหมด Manual: รับเฉพาะ Scanner USB + ทดสอบทันทีไม่รอ M3031

## ปัญหาเดิม
- โหมด Manual รับบาร์โค้ดได้ทั้ง Scanner USB และ Keyence
- สแกนแล้วต้องรอ M3031 = 1 ก่อนเริ่มทดสอบ เหมือนโหมด Auto

## สิ่งที่จะแก้
- **Manual**:
  - รับบาร์โค้ดจาก Scanner USB อย่างเดียว บาร์โค้ดจาก Keyence ถูกทิ้ง
  - สแกนแล้วเริ่ม Flow ชิ้นงาน (`RunPartFlowAsync`) ทันที ไม่รอ M3031 และไม่ใช้รอบ M3031
  - สแกนระหว่างที่กำลังทดสอบ → ไม่รับ (ไม่เก็บไว้รอ เพราะไม่มี M3031 มาเริ่มรอบถัดไป) ขึ้นป้าย "Busy - Scan Again"
  - M3031 เปลี่ยนสถานะ → จำค่าไว้เฉยๆ ไม่เริ่มทดสอบ
  - กฎ M3030 เหมือนเดิม (บาร์โค้ด ERROR / ยังไม่เลือก Line-Station-MO / ไม่มี Step / FAIL / Error → สั่ง M3030)
- **Auto**: ไม่เปลี่ยน — Keyence อย่างเดียว, รอ M3031 = 1 ก่อนทดสอบ
- ปุ่ม Start: ไม่เปลี่ยน

## ไฟล์ที่แก้
- `Themister_Real/Form2.cs` — `ProcessScannedBarcodeAsync`, `OnM3031Changed`, `TryStartPendingPartAsync` (แยกส่วนรันเป็น `StartPartAsync`), `KeyenceScanner_BarcodeReceived`
- `CLAUDE.md`, `README.md`
