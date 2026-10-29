# 2026-10-29 — เลิกยิง API routing-check ก่อนทดสอบ

## ปัญหาเดิม
- โหมด MES: พอ M3031 = 1 และมี SN แล้ว โปรแกรมจะ POST `{MES}/tdc/routing-check` ก่อน
  ป้ายขึ้น "Checking Routing..." ได้ `result = "OK"` ถึงจะทดสอบ
- ผู้ใช้ไม่ต้องการให้ยิง API routing-check อีกต่อไป

## สิ่งที่จะแก้
1. `RunPartFlowAsync`: ไม่เรียก routing-check แล้ว ไปที่ `RunSequenceAsync(sn)` เลย
2. ลบ `CheckRoutingAndShowWarningAsync` (ไม่มีที่อื่นเรียก)
3. ยังเก็บการตรวจ "ต้องเลือก Line, Station, MO ก่อน" ไว้ในโหมด MES โดยไม่ยิง API
   เพราะ routing-update ต้องใช้ค่าเหล่านี้ → ถ้ายังไม่เลือก: Popup + M3030 เหมือนเดิม
4. ไม่เปลี่ยนส่วนอื่น:
   - หลังทดสอบยังส่ง `routing-update` ไป MES เหมือนเดิม (PASS ส่งเลย / FAIL ถาม Popup)
   - บาร์โค้ด ERROR → FAIL + M3030 เหมือนเดิม
   - โหมด No MES ไม่ส่งอะไรไป MES เหมือนเดิม
5. อัปเดต `CLAUDE.md` (Flow + ตารางปัญหาที่แก้แล้ว)

## ไฟล์ที่แก้
- `Themister_Real/Form2.cs`
- `CLAUDE.md`
