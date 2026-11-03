# 2026-11-03 — Form2: ปรับ UI ให้ทันสมัย โทนสี Delta Electronics

## ปัญหาเดิม
- หน้าจอยังดูเป็น WinForms แบบเดิม: GroupBox มีเส้นขอบระบบ, Tab ระบบซ้อนกัน ("Manual" กับ "I/O" ชิดกัน), ไม่มีแถบหัวโปรแกรม
- ไฟสถานะ PLC / Thermistor ตอนเปิดโปรแกรมไม่มีสี (สั่ง BeginInvoke ก่อน Handle ถูกสร้าง) และเป็นสี่เหลี่ยมเล็กๆ
- ชื่อหน้าต่างเป็นแค่ "MES"

## สิ่งที่จะแก้ (ทำใน `ApplyTheme()` ของ `Form2.cs` ทั้งหมด ไม่แก้ `Form2.Designer.cs`)
1. ปรับชุดสีเป็นโทน Delta: Delta Blue `#005EB8` (ปุ่มหลัก / Tab ที่เลือก), Navy `#002D5A` (แถบหัว / หัวตาราง),
   พื้นหลัง `#EEF2F7`, การ์ดสีขาว ขอบ `#D8DEE6` ส่วนสีเขียว / แดง / ส้มของสถานะคงความหมายเดิม
2. เพิ่มแถบหัวโปรแกรม (Panel สร้างตอน Runtime, Dock Top) สี Navy:
   - ซ้าย: โลโก้สามเหลี่ยม + "DELTA" + ชื่อ "Thermistor Test Station"
   - ขวา: ย้าย `label16` / `pnPLCConnection` / `label17` / `pnThermistorConnection` ขึ้นมาไว้ในแถบนี้ (ตัวอักษรขาว)
   - ไฟสถานะเป็นวงกลม และตั้งสีแดงตั้งแต่เริ่ม (ต่อสำเร็จแล้วค่อยเป็นเขียวเหมือนเดิม)
3. GroupBox (PLC / MES) วาดเองเป็นการ์ด: พื้นขาว ขอบบาง หัวข้อตัวหนาสี Navy + ขีดเน้นสี Delta Blue
4. TabControl วาดเอง (OwnerDraw): Tab ขนาดเท่ากัน, Tab ที่เลือกพื้น Delta Blue ตัวขาว, Tab อื่นพื้นเทาอ่อน
5. ปุ่ม: Flat + สีเข้มขึ้นตอนเอาเมาส์ชี้ / กด
6. TextBox ขอบบาง (FixedSingle), ช่อง ReadOnly พื้นเทาอ่อน, ComboBox แบบ Flat
7. ค่า DMM (`lbSensorValue`) ตัวหนา, ตารางผล: หัวสี Navy แถวสูงขึ้น
8. ชื่อหน้าต่าง: "Thermistor Test Station — DELTA"
9. ไม่เปลี่ยน Logic / ชื่อ Control / การทำงานของระบบ Resize (Header ถูก Record และ Scale ตามเดิม)

## ไฟล์ที่แก้
- `Themister_Real/Form2.cs` (เฉพาะ Region UI Theme + ค่าเริ่มต้นไฟสถานะ)
- `README.md`, `CLAUDE.md`
