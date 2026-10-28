# 2026-10-29 — สแกนแล้วรอ M3031 ก่อน Check Routing + สั่ง M3030 ทุกกรณีที่ไม่ผ่าน

## ปัญหาเดิม
- สแกน QR แล้ว Check Routing ทันที ไม่รอให้ PLC แจ้งว่าชิ้นงานพร้อมที่ Station
- โหมด Auto ใช้ M3031 เป็นสัญญาณ "พร้อมส่ง MES" หลังทดสอบเสร็จ ส่วนโหมด Manual ใช้ขอบขาขึ้นของ M3031 เริ่มทดสอบโดยไม่ Check Routing
- Popup บางตัว (ไม่ได้เลือก Step / Recipe ว่าง / Error ระหว่างทดสอบ) จบโดยไม่สั่ง M3030 ชิ้นงานจึงค้างบนเครื่อง

## สิ่งที่แก้
1. สแกน (USB หรือ Keyence) → เก็บ SN ไว้ใน `_pendingBarcode` ป้ายขึ้น "Waiting M3031..."
2. เริ่ม Flow เมื่อมีทั้ง SN และ M3031 = 1 จะมาอะไรก่อนก็ได้
   - 1 รอบ M3031 ON = 1 ชิ้น ต้องรอ M3031 ลงเป็น 0 ก่อนจึงรับชิ้นถัดไป
   - Flow: บาร์โค้ด ERROR? → Check Routing (โหมด MES) → ทดสอบ → ส่ง routing-update → M3030
3. ใช้ทั้งโหมด Manual และ Auto (โหมดต่างกันแค่แหล่งสแกน) และเลิกรอ M3031 ก่อนส่ง MES (ลบ `SendRoutingWhenPlcReadyAsync` / `WaitForPlcSendReadyAsync`)
4. ทุกกรณีต่อไปนี้สั่ง M3030:
   - บาร์โค้ด ERROR
   - Routing ไม่ผ่าน / MES ล่ม / HTTP error / ยังไม่เลือก Line, Station, MO
   - ไม่ได้เลือก Step / Recipe ว่าง *(ใหม่)*
   - Error ระหว่างทดสอบ *(ใหม่)*
   - PASS → ส่ง MES แล้วสั่ง M3030
   - FAIL → กด OK หรือ Cancel ใน Popup ก็สั่ง M3030
   - ส่ง routing-update ไม่สำเร็จก็สั่ง M3030
5. ไม่สั่ง M3030 ใน 3 กรณีนี้:
   - ผู้ใช้กด Stop เอง
   - ยังไม่ผ่านการตรวจพนักงาน (ตรวจผ่านแล้วสแกนใหม่ได้ใน M3031 รอบเดิม)
   - PLC ไม่ได้เชื่อมต่อ
6. ปุ่ม Start บนหน้าจอยังทดสอบ SN ใน txtBarcode ได้ทันทีเหมือนเดิม โดยไม่รอ M3031 และไม่ Check Routing

## ไฟล์ที่แก้
- `Themister_Real/Form2.cs`
