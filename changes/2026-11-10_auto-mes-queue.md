# 2026-11-10 — โหมด Auto: PASS + ได้ QR สั่ง M3030 ทันที ส่ง MES ทีหลังตาม Delay (คิวขนาน) + ตารางคิวใต้ MES

## ปัญหาเดิม
- โหมด Auto รอ Delay → ส่ง MES → แล้วจึงสั่ง M3030 ชิ้นงานค้างที่ Station ระหว่างรอ ชิ้นถัดไปเข้าไม่ได้

## สิ่งที่จะแก้ (เฉพาะ Auto — Flow ที่เริ่มจาก M3031)
- **PASS** + ได้ QR → สั่ง M3030 ทันที → (โหมด MES) เข้าคิวรอส่ง MES เมื่อครบ "เวลาได้รับ QR + Delay"
- **FAIL** → **ไม่สั่ง M3030** (ชิ้นงานค้างไว้ให้คนเอาออก ป้าย "FAIL - Remove Part")
  - `cbPopup` = Popup → ถาม OK = เข้าคิวส่ง FAIL / Cancel = ไม่ส่ง
  - `cbPopup` = No Popup → เข้าคิวส่ง FAIL เลย
- QR "ERROR" / ไม่มี QR ใน 10 วิ → M3030 ไม่ส่ง MES (เหมือนเดิม)
- คิวทำงานขนาน — ชิ้นถัดไปทดสอบได้ทันที ไม่ต้องรอคิวส่งเสร็จ
- ส่งจากคิวใช้ `SendRoutingUpdateWithRetryAsync` (ไม่สำเร็จ → 0.5 วิ ส่งซ้ำ 1 ครั้ง)
- ตาราง `dgvMesQueue` ใต้ "Delay :" ในกล่อง MES: QR Code / Result / Values (Step=ค่าที่วัด) / Send in (s) / Status (Waiting → Sending → Sent / Failed) เก็บ 50 แถวล่าสุด
- Payload สร้างตอนทดสอบเสร็จ (ไม่ถูกกระทบเมื่อชิ้นถัดไปล้างตารางผลวัด หรือเปลี่ยน Line/Station/MO)
- ปิดโปรแกรมขณะยังมีรายการรอส่ง → ถามยืนยัน (คิวอยู่ในหน่วยความจำ ปิดแล้วหาย)
- Manual / ปุ่ม Start: ไม่เปลี่ยน (รอ Delay → ส่ง MES → M3030)

## ไฟล์ที่แก้
- `Themister_Real/Form2.Designer.cs` — เพิ่ม `dgvMesQueue`
- `Themister_Real/Form2.cs` — คิว MES (`PendingMesItem`, `EnqueueMesSend`, Timer, `SendQueuedMesAsync`), `RunSequenceAsync` (`useMesQueue`), `RunPartFlowAsync`, FormClosing
- `CLAUDE.md`, `README.md`
