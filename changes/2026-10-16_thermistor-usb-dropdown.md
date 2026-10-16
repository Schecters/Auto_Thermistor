# 2026-10-16 — Thermistor (USB DMM): เลือกอุปกรณ์ USB จาก Dropdown + เลิก Popup แจ้งหลุด

## ปัญหาเดิม
- โปรแกรมหา DMM Picotest จาก VID/PID ที่ฝังในโค้ด (`0x5710 / 0x3520`) เท่านั้น ผู้ใช้เลือกอุปกรณ์เองไม่ได้
- เมื่อสาย USB หลุด จะเด้ง MessageBox "USB DMM Disconnected" (Retry/Cancel) วนซ้ำ ขัดจังหวะการทำงาน

## สิ่งที่จะแก้ (Form2)
1. **แท็บ Connection** เพิ่มส่วน "Thermistor (USB)":
   - `cbThermistorUsb` (ComboBox DropDownList) แสดงอุปกรณ์ USB ที่เสียบอยู่ทั้งหมด (ชื่ออุปกรณ์ + VID/PID)
   - `btRefreshThermistorUsb` ปุ่ม Refresh รายการ
   - `btConnectThermistor` ปุ่ม Connect / Disconnect (สีเขียวเมื่อเชื่อมต่อแล้ว เหมือน Keyence)
2. ค้นหาอุปกรณ์ด้วย SetupAPI ตาม Interface GUID เดิม (`TARGET_DEVICE_GUIDS`) ตัดรายการซ้ำของอุปกรณ์เดียวกัน และอ่านชื่อจาก `SPDRP_FRIENDLYNAME` / `SPDRP_DEVICEDESC`
3. การเชื่อมต่อใช้ Device Path ของรายการที่เลือก แทนการหา VID/PID คงที่
   - บันทึกอุปกรณ์ที่เลือกไว้ใน `thermistor_config.cfg` เปิดโปรแกรมครั้งถัดไปจะเลือกและเชื่อมต่อให้อัตโนมัติ (เงียบ)
   - ถ้ายังไม่เคยบันทึก จะเลือก Picotest (VID 5710 / PID 3520) ให้ก่อนถ้าเจอ
4. **ลบ Popup แจ้ง USB DMM หลุด** (`ShowDmmDisconnectAlert`, `isShowingDmmAlert`) — สถานะดูจากไฟ `pnThermistorConnection` และปุ่ม Connect แทน
   - Monitor ทุก 2 วินาทียังทำงาน: ถ้าอุปกรณ์หายจะปิด Handle และขึ้นไฟแดง, ถ้าเสียบกลับมาและผู้ใช้ไม่ได้กด Disconnect จะต่อใหม่ให้เองแบบเงียบ
   - กด Disconnect แล้วจะไม่ต่อใหม่อัตโนมัติ (รวมถึงตอนรัน Step) จนกว่าจะกด Connect

## ไฟล์ที่แก้
- `Themister_Real/Form2.cs`
- `Themister_Real/Form2.Designer.cs`
