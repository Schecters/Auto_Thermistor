# 2026-11-10 — โหมด Auto: M3031 มาแล้วทดสอบทันที รอ Barcode ก่อนส่ง MES + ส่ง MES ซ้ำเมื่อไม่สำเร็จ

## ปัญหาเดิม
- โหมด Auto ต้องมีทั้ง SN จาก Keyence และ M3031 = 1 ก่อนจึงเริ่มทดสอบ — Keyence ช้า เครื่องเสียเวลารอ
- ส่ง MES ไม่สำเร็จ (ไม่ได้ HTTP 2xx / Exception) ครั้งเดียวก็จบ ยอดไม่ถูกนับ

## สิ่งที่จะแก้
- **Auto**: M3031 = 1 → เริ่มทดสอบทันที (มี SN รออยู่แล้วก็ใช้เลย)
  - ทดสอบเสร็จแล้วยังไม่มี SN → ป้าย "Waiting Barcode..." รอสูงสุด 10 วินาที
    - ได้ SN → ทำต่อตามเดิม (Log → PASS/FAIL → ส่ง MES → M3030)
    - ได้ "ERROR" → FAIL "Barcode Error" ไม่ส่ง MES + M3030
    - หมดเวลา → FAIL "No Barcode" ไม่ส่ง MES + M3030
    - กด Stop ระหว่างรอ → หยุด ไม่สั่ง M3030 (ตามกติกาเดิม)
  - โหมด No MES ก็รอ Barcode เหมือนกัน (ให้มี SN ใน Log)
  - Barcode ที่มาระหว่างทดสอบ ถ้าชิ้นนี้ยังไม่มี SN → ใช้เป็น SN ของชิ้นนี้; ถ้ามีแล้ว → เก็บไว้รอบถัดไปเหมือนเดิม
- **ส่ง MES ทุกโหมด**: ไม่สำเร็จ → รอ 0.5 วินาที ส่งซ้ำ 1 ครั้ง → แล้วจึงสั่ง M3030 (`SendRoutingUpdateWithRetryAsync`)
- ไม่เปลี่ยน: Manual (นอกจากส่ง MES ซ้ำ), ปุ่ม Start, Popup FAIL, ตัวกันค่าซ้ำ `_lastFinishedBarcode`

## ไฟล์ที่แก้
- `Themister_Real/Form2.cs` — `TryStartPendingPartAsync`, `StartPartAsync`, `RunPartFlowAsync`, `ProcessScannedBarcodeAsync`, `RunSequenceAsync`, เพิ่ม `WaitForPartBarcodeAsync`, `SendRoutingUpdateWithRetryAsync`
- `CLAUDE.md`, `README.md`
