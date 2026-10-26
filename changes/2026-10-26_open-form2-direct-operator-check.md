# 2026-10-26 — เปิด Form2 ทันที, ล็อก Line/Station/MO/MES, ตรวจสิทธิ์พนักงานผ่าน API

## ปัญหาเดิม
- โปรแกรมเปิดหน้า Login (`Login_Form`) ก่อนทุกครั้ง ผู้ใช้หน้างานต้องมีบัญชี ME/ADMIN จึงจะเข้าหน้าทดสอบได้
- เปลี่ยน `cbLines` `cbStations` `cbMO` `cbMES` ได้ตลอด
- ไม่ได้ตรวจว่าพนักงานที่ทำงานสอบผ่านสถานี THM แล้วหรือยัง

## สิ่งที่จะแก้
1. `Program.cs` เปิด `Form2` เป็นหน้าแรกทันที
2. ตอนเปิด Form2 `cbLines` `cbStations` `cbMO` `cbMES` ถูกล็อก (Enabled = false)
   - Line/Station ยังโหลดค่าล่าสุดจาก `line_station_config.cfg` เหมือนเดิม
   - API lineinfo ต้องมี `empNo` → ถ้ายังไม่ได้ Login ใช้ค่า default เดียวกับ molist (`86727533`)
3. เพิ่มปุ่ม `btSetting` ในกรอบ MES
   - กด → เปิด `Login_Form` เป็น Popup (ShowDialog) ใช้ API `/api/login` เดิม (สิทธิ์ ME/ADMIN)
   - Login ผ่าน → ปลดล็อก 4 ช่องนี้ ปุ่มเปลี่ยนเป็น "Lock"; กดอีกครั้งเพื่อล็อกกลับ
4. ผู้ใช้หน้างานกรอก/สแกนรหัสพนักงานที่ `txtEMPID` แล้วกด Enter
   - ส่ง `GET http://10.204.206.1:5000/api/operator-data?employee_id={txtEMPID}&lines={cbLines}&station=THM`
   - `summary.all_passed == true` → อนุญาตให้ทำงาน (ช่องเป็นสีเขียว แสดงชื่อพนักงาน)
   - ไม่ผ่าน → Popup แจ้งว่าไม่ผ่าน พร้อมรายการข้อสอบที่ไม่ผ่าน (exam_key, exam_name, score/min_score, status)
   - ระหว่างพิมพ์ใน `txtEMPID` ตัวดักบาร์โค้ด (KeyPreview) จะไม่ดักตัวอักษร
5. ยังไม่ผ่านการตรวจพนักงาน → สแกนบาร์โค้ด / กด Start ไม่ได้ (Popup แจ้งให้กรอกรหัสพนักงาน)
   - แก้ `txtEMPID` หรือเปลี่ยน Line → ต้องตรวจใหม่
   - รหัสพนักงานที่ผ่านใช้เป็น `empNo` ตอนส่งผล Routing ไป MES

ตัวอย่างผลจาก API:
```json
{
  "status": "pass", "employee_id": "86727533", "operator_name": "...",
  "lines": "STF_30", "station": "THM",
  "summary": { "total_exams": 1, "passed_exams": 1, "all_passed": true },
  "details": [ { "exam_key": "L2-QA-0001", "exam_name": "L2 - NC Control", "min_score": 18, "score": 20, "status": "PASS" } ]
}
```

## ไฟล์ที่แก้
- `Themister_Real/Program.cs`
- `Themister_Real/Form1.cs` (Login สำเร็จ → ปิด Popup ด้วย DialogResult.OK แทนการเปิด Form2)
- `Themister_Real/Form2.cs`, `Themister_Real/Form2.Designer.cs` (เพิ่ม `btSetting`)
