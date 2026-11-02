# 2026-11-02 — ส่ง MES ไม่สำเร็จ ไม่ต้องมี Popup (Popup FAIL คงไว้)

## ปัญหาเดิม
- `SendRoutingUpdateAsync` เด้ง Popup "MES Update Error" (HTTP ไม่ใช่ 2xx) และ "MES Connection Error" (Exception / Timeout)
- Popup เป็น Modal ทำให้ Flow ค้างจนกว่าจะมีคนกดปิด:
  - ยังไม่สั่ง M3030 ชิ้นงานจึงค้างที่ Station
  - Keyence/USB ยังส่งบาร์โค้ดเข้ามาได้ และ M3031 ยังเปลี่ยนได้ → SN ที่ส่ง MES ไม่ตรงกับชิ้นงานที่สแกน
  - USB Scanner ส่ง Enter ท้ายบาร์โค้ด → ไปกด OK ของ Popup และบาร์โค้ดหาย

## สิ่งที่จะแก้
1. `SendRoutingUpdateAsync`: ลบ `MessageBox.Show` ทั้ง 2 จุด
   - HTTP ไม่สำเร็จ → `UpdateSensorLabel("MES Send Failed (HTTP xxx)", Red)`
   - Exception → `UpdateSensorLabel("MES Send Failed", Red)`
   - ยังเขียน `mes_log.txt` เหมือนเดิม (Payload + HTTP/Error) และ return ค่าเดิม
2. ไม่แตะ:
   - Popup "Test Result: FAIL" (OK = ส่ง MES / Cancel = ไม่ส่ง)
   - Popup ยังไม่เลือก Line / Station / MO
   - Popup ตอนโหลด lineinfo / molist (เกิดตอนตั้งค่า ไม่ได้อยู่ใน Flow ชิ้นงาน)
3. อัปเดต `CLAUDE.md`

## ข้อควรรู้ (ยังไม่แก้รอบนี้)
- Popup FAIL ยังเป็น Modal ถ้าสแกนด้วย USB ตอน Popup เปิด ปุ่ม Enter ของ Scanner จะกด OK แทน

## ไฟล์ที่แก้
- `Themister_Real/Form2.cs`
- `README.md`, `CLAUDE.md`
