# 2026-10-20 — ซ่อน cbPopup (Popup / No Popup) ไว้ เปิดด้วยการคลิก "Modbus TCP" 3 ครั้ง

## ปัญหาเดิม
- `cbPopup` (เลือก Popup / No Popup) แสดงอยู่ตลอดในแท็บ Connection ผู้ใช้ทั่วไปเปลี่ยนค่าได้ง่าย

## สิ่งที่จะแก้ (Form2)
1. ตอนเปิดฟอร์ม ซ่อน `cbPopup` (`Visible = false`) ค่าเริ่มต้นยังเป็น "Popup" เหมือนเดิม
2. คลิกหัวข้อ `label15` ("Modbus TCP") **3 ครั้งติดกัน** (แต่ละครั้งห่างกันไม่เกิน ~1.5 วินาที) → แสดง `cbPopup` และเปิด Dropdown ให้เลือกทันที
   - ใช้ `MouseDown` นับคลิก เพราะ Label กลืน `Click` ครั้งที่ 2 เป็น `DoubleClick`
3. เมื่อเลือกค่าแล้ว (Dropdown ปิด) → ซ่อน `cbPopup` อีกครั้ง ค่าที่เลือกยังมีผลตามปกติ

## ไฟล์ที่แก้
- `Themister_Real/Form2.cs`
