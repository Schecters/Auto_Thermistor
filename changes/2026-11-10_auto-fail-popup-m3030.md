# 2026-11-10 — โหมด Auto: FAIL + Popup กด OK / Cancel แล้วสั่ง M3030 ดีดชิ้นงานออก

## ปัญหาเดิม
- โหมด Auto ผล FAIL ไม่สั่ง M3030 เลย แม้มีคนกดยืนยันใน Popup แล้ว → ต้องเอาชิ้นงานออกเอง

## สิ่งที่จะแก้
- Auto + FAIL + `cbPopup` = Popup → หลังกด OK (เข้าคิวส่ง FAIL) หรือ Cancel (ไม่ส่ง) → สั่ง M3030 ทั้งสองกรณี
- ข้อความ Popup เปลี่ยนเป็น "ทั้งสองกรณีจะปล่อยชิ้นงานออก"
- Auto + FAIL + No Popup → ไม่เปลี่ยน (เข้าคิวส่ง FAIL, ไม่สั่ง M3030)

## ไฟล์ที่แก้
- `Themister_Real/Form2.cs` — `RunSequenceAsync` ส่วน `useMesQueue`
- `CLAUDE.md`, `README.md`
