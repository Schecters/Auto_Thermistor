# 2026-10-15 — Form2: ปรับหน้าตา UI ให้สวยขึ้น

**ไฟล์ที่จะแก้:** `Themister_Real/Form2.cs`
**ไฟล์ที่ไม่แตะ:** `Form2.Designer.cs` (กัน Visual Studio Designer เขียนทับ)
**ผลต่อการทำงาน:** ไม่มี — แก้เฉพาะสี/ฟอนต์/สไตล์ ไม่เปลี่ยน logic PLC, DMM, Barcode, MES

## สิ่งที่จะทำ

เพิ่มเมธอด `ApplyTheme()` เรียกใน `Form2_Load` **ก่อน** `RecordInitialControlStates(this)`
เพื่อให้ระบบ auto-resize จำฟอนต์ใหม่เป็นค่าตั้งต้น

| ส่วน | ก่อน | หลัง |
|---|---|---|
| ฟอนต์ทั้งฟอร์ม | Microsoft Sans Serif | Segoe UI (ขนาดเดิม) |
| พื้นหลังฟอร์ม / Tab | สีเทา Control | โทนเทาอ่อนสะอาด + การ์ดสีขาว |
| GroupBox (PLC / MES) | ตัวหนังสือดำธรรมดา | หัวข้อตัวหนา สีน้ำเงินเข้ม |
| ปุ่ม On / Off (MN1–12) | ปุ่ม Windows ธรรมดา | Flat สีเขียว / แดง |
| ปุ่ม Start / Stop | ปุ่มธรรมดา | Flat สีเขียว / แดง ตัวหนา |
| ปุ่ม Connect / Refresh | ปุ่มธรรมดา | Flat สีน้ำเงิน / เทา |
| DataGridView ผลวัด | สไตล์ default | หัวตารางสีเข้ม, แถวสลับสี, ไม่มีเส้นตั้ง |
| ป้าย PASS / FAIL / READY | LimeGreen / Crimson / Gray | เขียว / แดง / เทา โทนเข้ม อ่านง่ายขึ้น, จัดกึ่งกลาง |
| ไฟสถานะ (PLC, Thermistor, MN) | LimeGreen / Crimson | คงเดิม (ไม่เปลี่ยน เพื่อไม่ให้สับสนหน้างาน) |

## ไม่ทำในรอบนี้
- ไม่ย้ายตำแหน่ง/ขนาด control (ระบบ resize อิงพิกัดเดิม)
- ไม่ refactor โค้ด
