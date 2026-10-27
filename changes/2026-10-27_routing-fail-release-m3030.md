# 2026-10-27 — Routing ไม่ผ่าน → สั่ง M3030 ปล่อยชิ้นงาน

## ปัญหาเดิม
- โหมด MES: ถ้า `routing-check` ไม่ผ่าน จะขึ้น Popup เตือนแล้วจบ ไม่เขียน M3030 ชิ้นงานจึงค้างอยู่บนเครื่อง
- บาร์โค้ด `ERROR` จาก Keyence ในโหมด MES ไม่ผ่าน `routing-check` จึงไม่ถูกปล่อยออกเหมือนกัน

## สิ่งที่จะแก้
- ใน `ProcessScannedBarcodeAsync` ถ้า `CheckRoutingAndShowWarningAsync` คืนค่า false ให้เรียก `TriggerPlcM3030Async()` เพื่อปล่อยชิ้นงาน
  - ใช้กับทุกกรณีที่คืนค่า false: Result ไม่ใช่ OK / HTTP error / เชื่อมต่อ MES ไม่ได้ / ยังไม่ได้เลือก Line, Station, MO
  - M3030 ถูกเขียนหลังผู้ใช้ปิด Popup เตือน Routing
- ใช้ทั้งโหมด Manual และ Auto
- ไม่ทดสอบ และไม่ส่ง routing-update

## ไฟล์ที่แก้
- `Themister_Real/Form2.cs`
