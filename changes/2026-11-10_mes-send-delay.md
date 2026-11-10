# 2026-11-10 — ตั้ง Delay ก่อนส่ง MES นับจากตอนได้รับ QR Code

## ต้องการ
- ตั้งได้ว่าหลังได้รับ QR Code (SN) ต้องรอกี่วินาทีจึงจะส่ง MES
- ช่องตั้งค่าอยู่ใต้ "MES :" ใน GroupBox MES มี Label "Delay :"

## สิ่งที่จะแก้
- เพิ่ม Control ใหม่ใน `Form2.Designer.cs` (ใต้ `cbMES`):
  - `lbMesDelay` ("Delay :") และ `numMesDelay` (NumericUpDown หน่วยวินาที 0–60 ทศนิยม 1 ตำแหน่ง ปรับทีละ 0.5)
- `numMesDelay` ล็อกเหมือน `cbMES` แก้ได้เฉพาะตอนปลดล็อกผ่าน btSetting (`ApplySettingLock`)
- จำค่าใน `mes_delay.cfg` (โหลดตอนเปิดโปรแกรม บันทึกเมื่อค่าเปลี่ยน)
- ส่ง MES (routing-update) ได้ก็ต่อเมื่อเลยเวลา "ได้รับ SN + Delay" แล้ว — ถ้าทดสอบนานกว่า Delay ส่งทันที ไม่รอเพิ่ม
  - เวลาที่ได้รับ SN: Manual = ตอนสแกน USB / Auto = ตอน Keyence ส่ง SN มา (ก่อนหรือระหว่างทดสอบ) / ปุ่ม Start = ตอนกด
  - ระหว่างรอขึ้นป้าย "MES Delay..." กด Stop ได้ (ไม่สั่ง M3030 ตามกติกาเดิม)
  - ใช้กับทุกจุดที่ส่ง MES (PASS, FAIL แบบ No Popup, FAIL Popup กด OK) ก่อนการส่งซ้ำ 0.5 วินาที
- Delay = 0 → ทำงานเหมือนเดิม

## ไฟล์ที่แก้
- `Themister_Real/Form2.Designer.cs` — เพิ่ม `lbMesDelay`, `numMesDelay`
- `Themister_Real/Form2.cs` — ตัวแปรเวลาได้รับ SN, โหลด/บันทึก `mes_delay.cfg`, `ApplySettingLock`, `RunSequenceAsync` (รอ Delay ก่อนส่ง MES)
- `CLAUDE.md`, `README.md`
