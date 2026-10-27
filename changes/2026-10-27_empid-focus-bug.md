# 2026-10-27 — แก้บั๊ก: ตรวจพนักงานผ่านแล้ว สแกนชิ้นงานด้วย Scanner USB ไม่ได้

## ปัญหาเดิม
1. กรอกรหัสพนักงานที่ `txtEMPID` แล้วกด Enter ผ่านการตรวจ แต่ Cursor ยังค้างอยู่ใน `txtEMPID`
2. ตัวดัก Scanner USB (`Form2_BarcodeKeyPress`) ข้ามทุกอย่างเมื่อ `txtEMPID.Focused` เพื่อให้พิมพ์รหัสพนักงานได้
3. เลยทำให้สแกนชิ้นงานต่อจากนั้น
   - บาร์โค้ดถูกพิมพ์ต่อท้ายลง `txtEMPID`
   - `TextChanged` ล้างผลตรวจพนักงาน (`verifiedOperatorId` ว่าง)
   - Enter ท้ายบาร์โค้ดส่ง `operator-data` ด้วยรหัสที่ผิด → Popup "Operator Check: FAIL"
   - ชิ้นงานไม่ถูกทดสอบ ต้องกรอกรหัสพนักงานใหม่

## สิ่งที่จะแก้
- ตรวจผ่าน (หลังกด OK ที่ Popup PASS) → ย้าย Focus ออกจาก `txtEMPID`
  - ไปที่ `txtBarcode` (ReadOnly) ถ้า Focus ได้ ไม่งั้นให้ฟอร์มไม่มี Control ไหน Focus (`ActiveControl = null`)
  - ตัวดัก Scanner USB จะกลับมาทำงาน
- ถ้าต้องการเปลี่ยนพนักงาน คลิกที่ `txtEMPID` แล้วพิมพ์ใหม่ได้เหมือนเดิม

## ไฟล์ที่แก้
- `Themister_Real/Form2.cs`
