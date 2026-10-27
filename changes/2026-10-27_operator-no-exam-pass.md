# 2026-10-27 — แก้บั๊ก: Station ที่ไม่มีข้อสอบบังคับ ตรวจพนักงานแล้วขึ้น FAIL

## ปัญหาเดิม
ถ้า Station ไม่มีข้อสอบบังคับ API `operator-data` ตอบกลับแบบนี้ (ลองกับรหัส 86727533, Line STF_30/31/32/33, Station THM)

```json
{
  "status": "pass", "employee_id": "86727533", "operator_name": "-",
  "lines": "STF_32", "station": "THM",
  "total_exams": 0, "passed_exams": 0,
  "message": "สถานีนี้ไม่มีข้อสอบบังคับ พนักงานผ่านเกณฑ์พร้อมเข้าปฏิบัติงาน",
  "details": []
}
```

- คำตอบนี้ไม่มี Object `summary` (ไม่มี `all_passed`)
- โค้ดตัดสินจาก `summary.all_passed` อย่างเดียว เลยขึ้น "Operator Check: FAIL" ทั้งที่ API บอกว่าผ่าน

## สิ่งที่จะแก้
- ถ้ามี `summary` → ใช้ `summary.all_passed == true` ตัดสินเหมือนเดิม
- ถ้าไม่มี `summary` → ใช้ `status == "pass"` ตัดสิน
- Popup PASS: ถ้า API มี `message` ให้แสดงข้อความนั้นด้วย
- จำนวนข้อสอบ ผ่าน x จาก y: อ่านจาก `summary` ถ้ามี ไม่งั้นอ่าน `passed_exams` / `total_exams` ที่ระดับบนสุดของคำตอบ

## ไฟล์ที่แก้
- `Themister_Real/Form2.cs`
