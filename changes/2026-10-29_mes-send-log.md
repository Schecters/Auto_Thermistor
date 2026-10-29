# 2026-10-29 — เขียน Log สิ่งที่ส่งไป MES แยกอีก 1 ไฟล์

## ปัญหาเดิม
- ตอนนี้มีแค่ `D:\Thermistor\yyyy\MM\dd\log.txt` เก็บ `test_data` ของแต่ละชิ้น
- ไม่มีบันทึกว่าส่งอะไรไป MES (`routing-update`) และ MES ตอบกลับอะไร → ตรวจย้อนหลังไม่ได้

## สิ่งที่จะแก้
1. เพิ่มเมธอด `SaveMesLog(...)` เขียนไฟล์ `mes_log.txt` ในโฟลเดอร์เดียวกับ `log.txt`
   (`D:\Thermistor\yyyy\MM\dd\mes_log.txt`)
2. เรียกใน `SendRoutingUpdateAsync` ทุกครั้งที่ส่ง บันทึก:
   - เวลา (`yyyy-MM-dd HH:mm:ss.fff`), URL
   - Payload JSON ที่ส่งไปทั้งก้อน
   - ผลตอบกลับ: HTTP Status + Response body หรือ Exception (เช่น Timeout / ต่อไม่ได้)
3. เขียนไฟล์พังต้องไม่ทำให้การทดสอบ/การส่ง MES พัง (try/catch เหมือน `SaveTestDataLog`)
4. ไม่เปลี่ยน Flow อื่น

## ตัวอย่างใน mes_log.txt
```
[2026-10-29 14:03:12.345] POST http://10.204.212.22:5207/api/mes/tdc/typed/routing-update
REQUEST : {...payload...}
RESPONSE: HTTP 200 {...}
```

## ไฟล์ที่แก้
- `Themister_Real/Form2.cs`
- `README.md` (Change log), `CLAUDE.md`
