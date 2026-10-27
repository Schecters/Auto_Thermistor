# 2026-10-27 — โหมด Manual / Auto (cbManualAuto): เลือกแหล่งสแกน + รอ M30 ก่อนส่ง MES

## ปัญหาเดิม
- รับบาร์โค้ดได้ทั้งจาก Scanner USB (Keyboard Wedge) และ Keyence (LAN) พร้อมกันตลอด
- ส่งผล Routing ไป MES ทันทีที่ทดสอบเสร็จ ไม่รอสัญญาณจาก PLC

## สิ่งที่จะแก้
1. `cbManualAuto` (หน้า tabPage5) เป็น Dropdown แบบ DropDownList มี `Manual` / `Auto` (ค่าเริ่มต้น `Manual`)
2. **Manual**
   - รับบาร์โค้ดจาก Scanner USB เท่านั้น
   - บาร์โค้ดจาก Keyence ถูกทิ้ง ไม่เข้า Flow
   - ส่ง MES ทันทีเหมือนเดิม
3. **Auto**
   - รับบาร์โค้ดจาก Keyence เท่านั้น
   - ตัวดัก Scanner USB ไม่ทำงาน ตัวอักษรที่พิมพ์ลงช่องอื่นได้ตามปกติ
   - ตรวจ Routing แล้วเริ่มทดสอบทันทีเหมือนเดิม
   - พอทดสอบเสร็จ ก่อนเรียก `SendRoutingUpdateAsync` ให้อ่าน M30 (Coil 30) จาก PLC ซ้ำทุก 100 ms จนได้ค่าเป็น 1 แล้วจึงส่ง API จากนั้นสั่ง M3030 ตามเดิม
   - ระหว่างรอ ป้ายสถานะขึ้นว่า "Waiting M30..." กด Stop เพื่อยกเลิกได้
   - กรณีไม่ต้องส่ง (No MES / FAIL แล้วกด Cancel) ไม่ต้องรอ M30
4. เปลี่ยนโหมดเมื่อไร ให้เคลียร์บัฟเฟอร์บาร์โค้ดทุกครั้ง

## ไฟล์ที่แก้
- `Themister_Real/Form2.cs`
- `Themister_Real/Form2.Designer.cs` (เพิ่ม `cbManualAuto` ในหน้า Designer แล้ว)
