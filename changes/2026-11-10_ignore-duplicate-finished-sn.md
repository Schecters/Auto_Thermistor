# 2026-11-10 — โหมด Auto: ไม่รับ SN ซ้ำของชิ้นที่เพิ่งทดสอบเสร็จ

## ปัญหาเดิม
- โหมด Auto บางครั้งชิ้นงานถูกทดสอบด้วย SN ของชิ้นก่อนหน้า แล้วเหลื่อมไปเรื่อย ๆ
- สาเหตุ: Keyence อ่านชิ้นเดิมซ้ำ แต่ค่าซ้ำมาถึง **หลัง** ทดสอบชิ้นนั้นเสร็จแล้ว
  (MES Bridge relay ถือ QR ไว้จนหน้า /MES ส่ง MES เสร็จ) ตอนนั้น `_processingBarcode` เป็น null แล้ว
  ค่าซ้ำจึงถูกเก็บใน `_pendingBarcode` และถูกใช้กับชิ้นถัดไปตอน M3031 ขึ้น

## สิ่งที่จะแก้
- เพิ่ม `_lastFinishedBarcode` จำ SN ของชิ้นที่ทดสอบเสร็จล่าสุด (ตั้งใน `finally` ของ `StartPartAsync` ก่อนล้าง `_processingBarcode`)
- `ProcessScannedBarcodeAsync` โหมด Auto: ถ้าบาร์โค้ดไม่ใช่ "ERROR" และตรงกับ `_lastFinishedBarcode` (ไม่สนตัวพิมพ์) → ทิ้ง ไม่ทำอะไร
- โหมด Manual / ปุ่ม Start: ไม่เปลี่ยน

## ไฟล์ที่แก้
- `Themister_Real/Form2.cs` — ตัวแปร `_lastFinishedBarcode`, `StartPartAsync`, `ProcessScannedBarcodeAsync`
- `README.md`
