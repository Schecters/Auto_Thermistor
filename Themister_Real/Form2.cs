using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;
using NModbus;

namespace Themister_Real
{
    public partial class Form2 : Form
    {
        // ---------------- Barcode Scanner Interceptor (Keyboard Wedge) ----------------
        private readonly StringBuilder _barcodeBuffer = new StringBuilder();
        private DateTime _lastKeystrokeTime = DateTime.MinValue;

        // ---------------- PLC Modbus TCP ----------------
        private TcpClient tcpClient = null;
        private IModbusMaster modbusMaster = null;
        private byte currentSlaveId = 1;

        // ควบคุม Loop Real-time Polling
        private CancellationTokenSource ctsPolling = null;
        private bool isPolling = false;

        // ควบคุม Loop Execution ของ Sequence Step
        private CancellationTokenSource ctsSequence = null;
        private bool isSequenceRunning = false;

        // Lock ป้องกันคำสั่งชนกันบน TCP Stream
        private readonly SemaphoreSlim modbusLock = new SemaphoreSlim(1, 1);
        private readonly string[] cachedMnAddresses = new string[12];
        private bool _prevM3031State = false;

        // ---------------- USB SCPI DMM (M3521A / M3522A) ----------------
        private SafeFileHandle usbDeviceHandle = null;
        private IntPtr winUsbHandle = IntPtr.Zero;
        private byte inPipeId = 0x81;
        private byte outPipeId = 0x02;
        private byte usbTmcBTag = 1;
        private readonly SemaphoreSlim dmmLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource ctsDmmMonitor = null;
        private bool isDmmMonitoring = false;
        private bool isShowingDmmAlert = false;

        // Hardware IDs สำหรับ PICOTEST M3521A / M3522A
        private const ushort TARGET_VID = 0x5710;
        private const ushort TARGET_PID = 0x3520;

        private readonly string configFilePath = Path.Combine(Application.StartupPath, "modbus_config.cfg");
        private readonly string mnConfigFilePath = Path.Combine(Application.StartupPath, "mn_address_config.cfg");
        private readonly string lineStationConfigFilePath = Path.Combine(Application.StartupPath, "line_station_config.cfg");
        private readonly string keyenceConfigFilePath = Path.Combine(Application.StartupPath, "keyence_config.cfg");

        // ---------------- Keyence Barcode Scanner (SR-X100W / TCP Data Port) ----------------
        private KeyenceScanner keyenceScanner = null;
        private bool isKeyenceConnecting = false;

        // ตัวแปรเก็บ Mapping ระหว่าง Line -> รายชื่อ Station ทั้งหมด
        private readonly Dictionary<string, List<string>> lineStationMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        // Model และ Dictionary เก็บข้อมูล Station -> Section & Group
        public class StationDetailModel
        {
            public string LineName { get; set; }
            public string SectionName { get; set; }
            public string GroupName { get; set; }
            public string StationName { get; set; }
        }

        private readonly Dictionary<string, StationDetailModel> stationInfoMap = new Dictionary<string, StationDetailModel>(StringComparer.OrdinalIgnoreCase);

        // Mapping: MO_NUMBER -> MODEL_NAME ของ Line ที่เลือกปัจจุบัน
        private readonly Dictionary<string, string> moModelMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // URL สำหรับ API Backend
        private const string API_PLC_SEQUENCE_URL = "http://10.204.206.1:5000/api/plc/sequence";
        private const string API_MES_BASE_URL = "http://10.204.212.22:5207/api/mes";

        public Form2()
        {
            InitializeComponent();

            this.KeyPreview = true;
            this.KeyPress += Form2_BarcodeKeyPress;

            this.FormClosed += Form2_FormClosed;
            InitControls();
            ApplyTheme();
        }

        #region Barcode Scanner Trap Logic & Routing Check (Global ReadOnly Interceptor)

        private async void Form2_BarcodeKeyPress(object sender, KeyPressEventArgs e)
        {
            TimeSpan elapsed = DateTime.Now - _lastKeystrokeTime;
            _lastKeystrokeTime = DateTime.Now;

            // หากตัวอักษรเว้นช่วงนานเกิน 60 ms แสดงว่าเริ่มชุดใหม่ ให้เคลียร์บัฟเฟอร์
            if (elapsed.TotalMilliseconds > 60)
            {
                _barcodeBuffer.Clear();
            }

            // ============================================================
            // 1. ตรวจจับปุ่ม ENTER (สัญลักษณ์ปิดท้ายจาก Auto Keyboard)
            // ============================================================
            if (e.KeyChar == (char)Keys.Return || e.KeyChar == (char)Keys.Enter)
            {
                if (_barcodeBuffer.Length > 0)
                {
                    string scannedBarcode = _barcodeBuffer.ToString().Trim();
                    _barcodeBuffer.Clear();

                    e.Handled = true;

                    await ProcessScannedBarcodeAsync(scannedBarcode);
                }
                return;
            }

            // ============================================================
            // 2. ดักเก็บตัวอักษรเข้าบัฟเฟอร์ และป้องกันข้อความหลุดไปช่องอื่น
            // ============================================================
            if (!char.IsControl(e.KeyChar))
            {
                _barcodeBuffer.Append(e.KeyChar);

                // หากตัวอักษรยิงเข้ามาด้วยความเร็วระดับเครื่องสแกน ให้ระงับไม่ให้พิมพ์ลง Control อื่น
                if (elapsed.TotalMilliseconds <= 60 && _barcodeBuffer.Length > 1)
                {
                    e.Handled = true;
                }
            }
        }

        // ใช้ร่วมกันทั้ง Keyboard Wedge Scanner และ Keyence Scanner (LAN)
        private async Task ProcessScannedBarcodeAsync(string scannedBarcode)
        {
            Control[] foundBarcode = this.Controls.Find("txtBarcode", true);
            if (foundBarcode.Length > 0 && foundBarcode[0] is TextBox txtBarcode)
            {
                txtBarcode.Text = scannedBarcode;
            }

            if (isSequenceRunning) return;

            // ตรวจสอบโหมด MES / No MES จาก ComboBox cbMES โดยตรง
            string mesMode = cbMES.SelectedItem?.ToString() ?? "MES";

            if (string.Equals(mesMode, "No MES", StringComparison.OrdinalIgnoreCase))
            {
                btPLCStart_Click(this, EventArgs.Empty);
            }
            else
            {
                bool isRoutingPass = await CheckRoutingAndShowWarningAsync(scannedBarcode);
                if (isRoutingPass && !isSequenceRunning)
                {
                    btPLCStart_Click(this, EventArgs.Empty);
                }
            }
        }

        public async Task<bool> CheckRoutingAndShowWarningAsync(string serialNo)
        {
            if (string.IsNullOrWhiteSpace(serialNo)) return false;

            string mo = "";
            Control[] foundMo = this.Controls.Find("cbMO", true);
            if (foundMo.Length > 0 && foundMo[0] is ComboBox cbMo)
            {
                mo = cbMo.SelectedItem?.ToString() ?? "";
            }

            string model = "";
            Control[] foundModel = this.Controls.Find("txtModel", true);
            if (foundModel.Length > 0 && foundModel[0] is TextBox txtModel)
            {
                model = txtModel.Text.Trim();
            }

            string line = cbLines.SelectedItem?.ToString() ?? "";
            string station = cbStations.SelectedItem?.ToString() ?? "";

            string section = "";
            Control[] foundSection = this.Controls.Find("txtSection", true);
            if (foundSection.Length > 0 && foundSection[0] is TextBox txtSection)
            {
                section = txtSection.Text.Trim();
            }

            string group = "";
            Control[] foundGroup = this.Controls.Find("txtGroup", true);
            if (foundGroup.Length > 0 && foundGroup[0] is TextBox txtGroup)
            {
                group = txtGroup.Text.Trim();
            }

            if (string.IsNullOrEmpty(line) || string.IsNullOrEmpty(station) || string.IsNullOrEmpty(mo))
            {
                UpdateSensorLabel("No Route", Color.Red);

                MessageBox.Show("กรุณาเลือก Line, Station และ MO ให้เรียบร้อยก่อนทำการสแกนบาร์โค้ด",
                                "MES Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            UpdateSensorLabel("Checking Routing...", Color.Orange);

            var payload = new
            {
                sn = serialNo.Trim(),
                mo = mo,
                model = model,
                line = line,
                section = section,
                group = group,
                station = station,
                testSubProgram = "",
                testSubVersion = "",
                testData = ""
            };

            string url = $"{API_MES_BASE_URL}/tdc/routing-check";

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    string jsonBody = JsonSerializer.Serialize(payload);
                    var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                    HttpResponseMessage response = await client.PostAsync(url, content);
                    string resContent = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        using (JsonDocument doc = JsonDocument.Parse(resContent))
                        {
                            string result = doc.RootElement.TryGetProperty("result", out JsonElement rElem) ? rElem.GetString() : "";
                            string desc = doc.RootElement.TryGetProperty("description", out JsonElement dElem) ? dElem.GetString() : "N/A";

                            if (result.Equals("OK", StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }
                            else
                            {
                                UpdateSensorLabel("No Route", Color.Red);

                                MessageBox.Show($"ชิ้นงานไม่ผ่านเงื่อนไข Routing:\nSN: {serialNo}\nResult: {result}\nDescription: {desc}",
                                                "MES Routing Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                return false;
                            }
                        }
                    }
                    else
                    {
                        UpdateSensorLabel("No Route", Color.Red);

                        MessageBox.Show($"เซิร์ฟเวอร์แจ้งข้อผิดพลาด (HTTP {(int)response.StatusCode}):\n{resContent}",
                                        "MES Connection Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateSensorLabel("No Route", Color.Red);

                MessageBox.Show($"ไม่สามารถเชื่อมต่อไปยัง MES Server เพื่อตรวจ Routing ได้:\n{ex.Message}",
                                "MES Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private string BuildUpdateRouteJson(string currentSn, bool isPass)
        {
            string sn = string.IsNullOrEmpty(currentSn) ? "" : currentSn.Trim();
            string mo = cbMO.SelectedItem?.ToString() ?? "";
            string model = string.IsNullOrEmpty(txtModel.Text.Trim()) ? "" : txtModel.Text.Trim();
            string line = cbLines.SelectedItem?.ToString() ?? "";
            string station = cbStations.SelectedItem?.ToString() ?? "";
            string section = string.IsNullOrEmpty(txtSection.Text.Trim()) ? "" : txtSection.Text.Trim();
            string group = string.IsNullOrEmpty(txtGroup.Text.Trim()) ? "" : txtGroup.Text.Trim();
            string empNo = !string.IsNullOrEmpty(UserSession.EmployeeId) ? UserSession.EmployeeId : "";
            string resultStr = isPass ? "PASS" : "FAIL";

            string routingString = $"{sn}}}{mo}}}{model}}}{line}}}{section}}}{group}}}{station}}}}}{resultStr}}}{empNo}}}1";

            string startTimeStr = DateTime.Now.ToString("yyMMdd_HH:mm:ss:ff");
            string endTimeStr = DateTime.Now.ToString("yyMMdd_HH:mm:ss:ff");
            List<string> stepResults = new List<string>();

            if (dgvSensorResults != null && dgvSensorResults.Rows.Count > 0)
            {
                var firstRow = dgvSensorResults.Rows[0];
                var lastRow = dgvSensorResults.Rows[dgvSensorResults.Rows.Count - 1];

                if (firstRow.Cells["colTimeStart"].Value != null)
                {
                    string tStart = firstRow.Cells["colTimeStart"].Value.ToString();
                    startTimeStr = $"{DateTime.Now:yyMMdd}_{tStart.Replace('.', ':')}";
                }

                if (lastRow.Cells["colTimeEnd"].Value != null)
                {
                    string tEnd = lastRow.Cells["colTimeEnd"].Value.ToString();
                    endTimeStr = $"{DateTime.Now:yyMMdd}_{tEnd.Replace('.', ':')}";
                }

                foreach (DataGridViewRow row in dgvSensorResults.Rows)
                {
                    string stepName = row.Cells["colStep"].Value?.ToString() ?? "";
                    string measured = row.Cells["colMeasured"].Value?.ToString() ?? "";
                    string stepStart = row.Cells["colTimeStart"].Value?.ToString() ?? "";
                    string stepEnd = row.Cells["colTimeEnd"].Value?.ToString() ?? "";

                    measured = measured.Replace("Ω", "").Replace("Ohm", "").Replace("ohm", "")
                                       .Replace("Word", "").Replace("word", "").Trim();
                    stepStart = stepStart.Replace('.', ':');
                    stepEnd = stepEnd.Replace('.', ':');

                    stepResults.Add($"{stepName},{measured},{stepStart},{stepEnd}");
                }
            }

            string stepsSummary = stepResults.Count > 0
                ? string.Join(";", stepResults)
                : $"MAIN_RESIST,0.0000,{DateTime.Now:HH:mm:ss:ff},{DateTime.Now:HH:mm:ss:ff}";

            string testDataFormatted = $"START_{startTimeStr};{sn};{stepsSummary};END_{endTimeStr};{resultStr}";

            return JsonSerializer.Serialize(new
            {
                routingData = new[] { routingString },
                testData = testDataFormatted
            }, new JsonSerializerOptions { WriteIndented = true });
        }

        #endregion

        private void InitControls()
        {
            cbLines.DropDownStyle = ComboBoxStyle.DropDownList;
            cbStations.DropDownStyle = ComboBoxStyle.DropDownList;

            cbLines.Items.Clear();
            cbStations.Items.Clear();
            cbStations.Enabled = false;

            cbLines.SelectedIndexChanged += cbLines_SelectedIndexChanged;
            cbStations.SelectedIndexChanged += cbStations_SelectedIndexChanged;

            Control[] foundBarcode = this.Controls.Find("txtBarcode", true);
            if (foundBarcode.Length > 0 && foundBarcode[0] is TextBox txtBarcode)
            {
                txtBarcode.ReadOnly = true;
                txtBarcode.BackColor = SystemColors.Window;
            }

            Control[] foundCbPopup = this.Controls.Find("cbPopup", true);
            if (foundCbPopup.Length > 0 && foundCbPopup[0] is ComboBox cbPopup)
            {
                cbPopup.DropDownStyle = ComboBoxStyle.DropDownList;
                cbPopup.Items.Clear();
                cbPopup.Items.AddRange(new object[] { "Popup", "No Popup" });
                cbPopup.SelectedIndex = 0;
            }

            cbMES.DropDownStyle = ComboBoxStyle.DropDownList;
            cbMES.Items.Clear();
            cbMES.Items.AddRange(new object[] { "MES", "No MES" });
            cbMES.SelectedIndex = 1;
            cbMES.SelectedIndexChanged += (s, e) =>
            {
                if (foundBarcode.Length > 0 && foundBarcode[0] is TextBox txt)
                {
                    txt.Clear();
                }
            };

            if (this.Controls.Find("cbMO", true).Length > 0 && this.Controls.Find("cbMO", true)[0] is ComboBox cbMoControl)
            {
                cbMoControl.DropDownStyle = ComboBoxStyle.DropDownList;
                cbMoControl.SelectedIndexChanged += cbMO_SelectedIndexChanged;
            }

            if (cbSlave.Items.Count == 0)
            {
                cbSlave.Items.AddRange(new object[] { "1", "2", "3", "4", "5" });
                cbSlave.SelectedIndex = 0;
            }

            if (cbStep != null)
            {
                cbStep.DropDownStyle = ComboBoxStyle.DropDownList;
            }

            if (btPLCStart != null) btPLCStart.Click += btPLCStart_Click;
            if (btPLCStop != null) btPLCStop.Click += btPLCStop_Click;
            if (btStepRefresh != null) btStepRefresh.Click += btStepRefresh_Click;
            if (btConnectKeynce != null) btConnectKeynce.Click += btConnectKeynce_Click;

            Control[] foundApiTxt = this.Controls.Find("txtAPI", true);
            if (foundApiTxt.Length > 0 && foundApiTxt[0] is TextBox txtApi)
            {
                txtApi.Multiline = true;
                txtApi.ScrollBars = ScrollBars.Vertical;
                txtApi.WordWrap = true;
            }

            for (int i = 1; i <= 12; i++)
            {
                int index = i - 1;
                Control[] foundTxt = this.Controls.Find($"txtMN{i}", true);
                if (foundTxt.Length > 0 && foundTxt[0] is TextBox txt)
                {
                    txt.Leave += (s, e) => SaveMNAddresses();
                    txt.TextChanged += (s, e) =>
                    {
                        cachedMnAddresses[index] = txt.Text.Trim();
                        SaveMNAddresses();
                    };
                }

                Control[] foundBtnOn = this.Controls.Find($"btONMN{i}", true);
                if (foundBtnOn.Length > 0 && foundBtnOn[0] is Button btnOn)
                {
                    btnOn.Click += BtnONMN_Click;
                }

                Control[] foundBtnOff = this.Controls.Find($"btOFFMN{i}", true);
                if (foundBtnOff.Length > 0 && foundBtnOff[0] is Button btnOff)
                {
                    btnOff.Click += BtnOFFMN_Click;
                }
            }

            if (dgvSensorResults != null)
            {
                dgvSensorResults.Columns.Clear();
                dgvSensorResults.ReadOnly = true;
                dgvSensorResults.AllowUserToAddRows = false;
                dgvSensorResults.AllowUserToDeleteRows = false;
                dgvSensorResults.RowHeadersVisible = false;
                dgvSensorResults.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
                dgvSensorResults.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

                dgvSensorResults.Columns.Add("colStep", "Step");
                dgvSensorResults.Columns.Add("colSpec", "Spec");
                dgvSensorResults.Columns.Add("colMeasured", "Measured");
                dgvSensorResults.Columns.Add("colResult", "Result");
                dgvSensorResults.Columns.Add("colTimeStart", "Start Time");
                dgvSensorResults.Columns.Add("colTimeEnd", "End Time");
            }

            ResetAllPanelsToGray();
            UpdatePlcStatusIndicator(false);
            UpdateDmmStatusIndicator(false);
            UpdateSensorLabel("0.0000", Color.Gray);

            SetPassFailReady();
        }

        #region UI Theme

        // โทนสีหลักของหน้าจอ (ไฟสถานะ PLC/MN ยังใช้ LimeGreen/Crimson เดิม)
        private static readonly Color ThemeBackground = Color.FromArgb(243, 244, 246);
        private static readonly Color ThemeCard = Color.White;
        private static readonly Color ThemeText = Color.FromArgb(31, 41, 55);
        private static readonly Color ThemeHeader = Color.FromArgb(30, 58, 138);
        private static readonly Color ThemePrimary = Color.FromArgb(37, 99, 235);
        private static readonly Color ThemeSuccess = Color.FromArgb(22, 163, 74);
        private static readonly Color ThemeDanger = Color.FromArgb(220, 38, 38);
        private static readonly Color ThemeNeutral = Color.FromArgb(107, 114, 128);
        private static readonly Color ThemeWarning = Color.FromArgb(217, 119, 6);
        private static readonly Color ThemeGridLine = Color.FromArgb(229, 231, 235);
        private const string ThemeFontName = "Segoe UI";

        // ต้องเรียกก่อน RecordInitialControlStates() เพื่อให้ระบบ Resize จำฟอนต์ใหม่เป็นค่าตั้งต้น
        private void ApplyTheme()
        {
            this.BackColor = ThemeBackground;
            ApplyThemeToChildren(this);

            if (dgvSensorResults != null)
            {
                dgvSensorResults.BackgroundColor = ThemeCard;
                dgvSensorResults.BorderStyle = BorderStyle.None;
                dgvSensorResults.GridColor = ThemeGridLine;
                dgvSensorResults.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
                dgvSensorResults.EnableHeadersVisualStyles = false;
                dgvSensorResults.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
                dgvSensorResults.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
                dgvSensorResults.ColumnHeadersHeight = 32;
                dgvSensorResults.ColumnHeadersDefaultCellStyle.BackColor = ThemeHeader;
                dgvSensorResults.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
                dgvSensorResults.ColumnHeadersDefaultCellStyle.SelectionBackColor = ThemeHeader;
                dgvSensorResults.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                dgvSensorResults.DefaultCellStyle.ForeColor = ThemeText;
                dgvSensorResults.DefaultCellStyle.SelectionBackColor = Color.FromArgb(219, 234, 254);
                dgvSensorResults.DefaultCellStyle.SelectionForeColor = ThemeText;
                dgvSensorResults.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                dgvSensorResults.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(249, 250, 251);
            }

            if (lbPassFail != null)
            {
                lbPassFail.AutoSize = false;
                lbPassFail.TextAlign = ContentAlignment.MiddleCenter;
                lbPassFail.ForeColor = Color.White;
                lbPassFail.TextChanged += (s, e) => FitPassFailFont();
                lbPassFail.SizeChanged += (s, e) => FitPassFailFont();
                FitPassFailFont();
            }
        }

        // หาขนาดฟอนต์ใหญ่สุดที่ข้อความ (READY / TEST / PASS / FAIL) ยังพอดีกล่อง lbPassFail
        private void FitPassFailFont()
        {
            if (lbPassFail == null || string.IsNullOrEmpty(lbPassFail.Text)) return;

            const int padding = 8;
            Size available = new Size(lbPassFail.ClientSize.Width - padding * 2, lbPassFail.ClientSize.Height - padding * 2);
            if (available.Width <= 0 || available.Height <= 0) return;

            float size = 8f;
            for (float candidate = 140f; candidate > 8f; candidate -= 2f)
            {
                using (var test = new Font(ThemeFontName, candidate, FontStyle.Bold))
                {
                    Size measured = TextRenderer.MeasureText(lbPassFail.Text, test, Size.Empty, TextFormatFlags.NoPadding);
                    if (measured.Width <= available.Width && measured.Height <= available.Height)
                    {
                        size = candidate;
                        break;
                    }
                }
            }

            if (Math.Abs(lbPassFail.Font.Size - size) > 0.1f || !lbPassFail.Font.Bold)
            {
                lbPassFail.Font = new Font(ThemeFontName, size, FontStyle.Bold);
            }
        }

        private void SetPassFailReady()
        {
            lbPassFail.Text = "READY";
            lbPassFail.ForeColor = Color.White;
            lbPassFail.BackColor = ThemeNeutral;
        }

        private void ApplyThemeToChildren(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                c.Font = new Font(ThemeFontName, c.Font.Size, c.Font.Style);

                if (c is GroupBox gb)
                {
                    gb.BackColor = ThemeCard;
                    gb.ForeColor = ThemeHeader;
                    gb.Font = new Font(ThemeFontName, gb.Font.Size, FontStyle.Bold);
                }
                else if (c is TabPage tp)
                {
                    tp.UseVisualStyleBackColor = false;
                    tp.BackColor = ThemeCard;
                }
                else if (c is Button btn)
                {
                    StyleButton(btn, GetButtonColor(btn.Name));
                }
                else if (c is Label || c is TextBox || c is ComboBox)
                {
                    if (c.Name != "lbPassFail") c.ForeColor = ThemeText;
                }

                if (c.HasChildren)
                {
                    ApplyThemeToChildren(c);
                }
            }
        }

        private static Color GetButtonColor(string name)
        {
            if (name.StartsWith("btONMN") || name == "btPLCStart") return ThemeSuccess;
            if (name.StartsWith("btOFFMN") || name == "btPLCStop") return ThemeDanger;
            if (name == "btStepRefresh") return ThemeNeutral;
            return ThemePrimary;
        }

        private static void StyleButton(Button btn, Color color)
        {
            btn.UseVisualStyleBackColor = false;
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 0;
            btn.BackColor = color;
            btn.ForeColor = Color.White;
            btn.Font = new Font(ThemeFontName, btn.Font.Size, FontStyle.Bold);
            btn.Cursor = Cursors.Hand;
        }

        #endregion

        private async void Form2_Load(object sender, EventArgs e)
        {
            this.StartPosition = FormStartPosition.CenterScreen;
            this.ClientSize = new Size(1590, 658);

            if (!File.Exists(configFilePath))
            {
                txtIP.Text = "192.168.1.30";
                txtPort.Text = "502";
                cbSlave.SelectedIndex = 0;
                SaveModbusSettings();
            }
            else
            {
                LoadModbusSettings();
            }

            if (!File.Exists(mnConfigFilePath))
            {
                SaveMNAddresses();
            }
            else
            {
                LoadMNAddresses();
            }

            for (int i = 1; i <= 12; i++)
            {
                Control[] found = this.Controls.Find($"txtMN{i}", true);
                if (found.Length > 0 && found[0] is TextBox txt)
                {
                    cachedMnAddresses[i - 1] = txt.Text.Trim();
                }
            }

            try
            {
                if (txtIP != null)
                {
                    txtIP.ReadOnly = false;
                    txtIP.Enabled = true;
                    txtIP.BringToFront();
                }
                if (txtPort != null)
                {
                    txtPort.ReadOnly = false;
                    txtPort.Enabled = true;
                    txtPort.BringToFront();
                }

                for (int i = 1; i <= 12; i++)
                {
                    Control[] foundTxt = this.Controls.Find($"txtMN{i}", true);
                    if (foundTxt.Length > 0 && foundTxt[0] is TextBox txt)
                    {
                        txt.ReadOnly = false;
                        txt.Enabled = true;
                        txt.BringToFront();
                    }
                }
            }
            catch { }

            _ = ConnectPlcAsync(isSilent: true);

            LoadKeyenceSettings();
            _ = ConnectKeyenceAsync(isSilent: true);

            StartDmmRealtimeMonitor();

            await LoadAllLinesAndStationsAsync();
            RestoreLastSelectedLineAndStation();
            await LoadStepRecipesAsync();

            _originalFormSize = new Size(1590, 658);
            RecordInitialControlStates(this);

            this.Resize += Form2_Resize;
        }

        #region Line, Station, Group, Section & MO API Integration

        private async Task LoadAllLinesAndStationsAsync()
        {
            string empNo = UserSession.EmployeeId;
            cbLines.Enabled = false;
            cbStations.Enabled = false;

            try
            {
                lineStationMap.Clear();
                stationInfoMap.Clear();
                cbLines.Items.Clear();
                cbStations.Items.Clear();

                await FetchAllLineDataFromApiAsync(empNo);

                if (lineStationMap.Count > 0)
                {
                    List<string> lines = new List<string>(lineStationMap.Keys);
                    lines.Sort();

                    cbLines.BeginUpdate();
                    foreach (var line in lines)
                    {
                        cbLines.Items.Add(line);
                    }
                    cbLines.EndUpdate();

                    cbLines.Enabled = true;
                    cbLines.SelectedIndex = -1;
                }
                else
                {
                    MessageBox.Show("ไม่พบข้อมูล Line จาก API", "แจ้งเตือน", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"เกิดข้อผิดพลาดในการโหลด Line/Station (ALL):\n{ex.Message}", "API Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                cbLines.Enabled = cbLines.Items.Count > 0;
            }
        }

        private async Task FetchAllLineDataFromApiAsync(string empNo)
        {
            string url = $"{API_MES_BASE_URL}/query/lineinfo?empNo={empNo}&line=ALL";

            using (HttpClient client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(15);
                HttpResponseMessage response = await client.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    var objectMatches = Regex.Matches(jsonResponse, @"\{[^{}]*\}");

                    foreach (Match objMatch in objectMatches)
                    {
                        string block = objMatch.Value;
                        var lineMatch = Regex.Match(block, "\"LINE(?:_NAME)?\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
                        var stationMatch = Regex.Match(block, "\"STATION(?:_NAME)?\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
                        var sectionMatch = Regex.Match(block, "\"SECTION(?:_NAME)?\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
                        var groupMatch = Regex.Match(block, "\"GROUP(?:_NAME)?\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);

                        if (lineMatch.Success && stationMatch.Success)
                        {
                            string line = lineMatch.Groups[1].Value.Trim();
                            string station = stationMatch.Groups[1].Value.Trim();
                            string section = sectionMatch.Success ? sectionMatch.Groups[1].Value.Trim() : "";
                            string group = groupMatch.Success ? groupMatch.Groups[1].Value.Trim() : "";

                            if (!string.IsNullOrEmpty(line) && !string.IsNullOrEmpty(station))
                            {
                                if (!lineStationMap.ContainsKey(line))
                                {
                                    lineStationMap[line] = new List<string>();
                                }

                                if (!lineStationMap[line].Contains(station))
                                {
                                    lineStationMap[line].Add(station);
                                }

                                string mapKey = $"{line}_{station}";
                                stationInfoMap[mapKey] = new StationDetailModel
                                {
                                    LineName = line,
                                    StationName = station,
                                    SectionName = section,
                                    GroupName = group
                                };
                            }
                        }
                    }

                    foreach (var key in lineStationMap.Keys)
                    {
                        lineStationMap[key].Sort();
                    }
                }
                else
                {
                    throw new Exception($"Server ตอบกลับด้วยรหัส: {response.StatusCode}");
                }
            }
        }

        private async void cbLines_SelectedIndexChanged(object sender, EventArgs e)
        {
            SetTextControl("txtGroup", "");
            SetTextControl("txtSection", "");

            if (cbLines.SelectedItem == null)
            {
                cbStations.Items.Clear();
                cbStations.Enabled = false;
                ClearMoAndModelControls();
                return;
            }

            string selectedLine = cbLines.SelectedItem.ToString();

            cbStations.BeginUpdate();
            cbStations.Items.Clear();

            if (lineStationMap.TryGetValue(selectedLine, out List<string> stations) && stations.Count > 0)
            {
                foreach (var st in stations)
                {
                    cbStations.Items.Add(st);
                }

                cbStations.EndUpdate();
                cbStations.Enabled = true;
                cbStations.SelectedIndex = -1;
            }
            else
            {
                cbStations.EndUpdate();
                cbStations.Enabled = false;
            }

            await LoadMoListBySelectedLineAsync(selectedLine);
        }

        private void cbStations_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cbLines.SelectedItem == null || cbStations.SelectedItem == null)
            {
                SetTextControl("txtGroup", "");
                SetTextControl("txtSection", "");
                return;
            }

            string selectedLine = cbLines.SelectedItem.ToString();
            string selectedStation = cbStations.SelectedItem.ToString();
            string mapKey = $"{selectedLine}_{selectedStation}";

            if (stationInfoMap.TryGetValue(mapKey, out StationDetailModel detail))
            {
                SetTextControl("txtGroup", detail.GroupName);
                SetTextControl("txtSection", detail.SectionName);
            }
            else
            {
                SetTextControl("txtGroup", "");
                SetTextControl("txtSection", "");
            }

            SaveLastSelectedLineAndStation();
        }

        private void SetTextControl(string controlName, string value)
        {
            Control[] found = this.Controls.Find(controlName, true);
            if (found.Length > 0 && found[0] is TextBox txt)
            {
                txt.Text = value;
            }
        }

        private async Task LoadMoListBySelectedLineAsync(string targetLineName)
        {
            ClearMoAndModelControls();

            string empNo = !string.IsNullOrEmpty(UserSession.EmployeeId) ? UserSession.EmployeeId : "86727533";
            string url = $"{API_MES_BASE_URL}/query/molist?empNo={empNo}&mode=0&moType=0&line=0";

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(15);
                    HttpResponseMessage response = await client.GetAsync(url);

                    if (response.IsSuccessStatusCode)
                    {
                        string jsonResponse = await response.Content.ReadAsStringAsync();

                        using (JsonDocument doc = JsonDocument.Parse(jsonResponse))
                        {
                            JsonElement root = doc.RootElement;
                            JsonElement dataArray = default;

                            if (root.ValueKind == JsonValueKind.Array)
                            {
                                dataArray = root;
                            }
                            else if (root.ValueKind == JsonValueKind.Object)
                            {
                                foreach (JsonProperty prop in root.EnumerateObject())
                                {
                                    if (prop.Value.ValueKind == JsonValueKind.Array)
                                    {
                                        dataArray = prop.Value;
                                        break;
                                    }
                                }
                            }

                            if (dataArray.ValueKind == JsonValueKind.Array)
                            {
                                foreach (JsonElement item in dataArray.EnumerateArray())
                                {
                                    string lineInJson = "";
                                    string moNumber = "";
                                    string modelName = "";

                                    foreach (JsonProperty prop in item.EnumerateObject())
                                    {
                                        string key = prop.Name.ToUpper();
                                        string val = prop.Value.ToString().Trim();

                                        if (key == "LINE" || key == "LINE_NAME" || key == "PDLINE_NAME")
                                            lineInJson = val;
                                        else if (key == "MO_NUMBER" || key == "MO" || key == "MONUMBER")
                                            moNumber = val;
                                        else if (key == "MODEL_NAME" || key == "MODEL" || key == "MODELNAME")
                                            modelName = val;
                                    }

                                    if (string.Equals(lineInJson, targetLineName.Trim(), StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (!string.IsNullOrEmpty(moNumber) && !moModelMap.ContainsKey(moNumber))
                                        {
                                            moModelMap[moNumber] = modelName;
                                        }
                                    }
                                }
                            }
                        }

                        Control[] foundMo = this.Controls.Find("cbMO", true);
                        if (foundMo.Length > 0 && foundMo[0] is ComboBox cbMo)
                        {
                            cbMo.BeginUpdate();
                            cbMo.Items.Clear();

                            foreach (var mo in moModelMap.Keys)
                            {
                                cbMo.Items.Add(mo);
                            }

                            cbMo.EndUpdate();
                            cbMo.Enabled = cbMo.Items.Count > 0;

                            if (cbMo.Items.Count > 0)
                            {
                                cbMo.SelectedIndex = 0;
                            }
                        }
                    }
                    else
                    {
                        MessageBox.Show($"ดึงข้อมูล MO List ล้มเหลว (HTTP Status: {response.StatusCode})", "API Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"เกิดข้อผิดพลาดในการโหลด MO List:\n{ex.Message}", "API Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void cbMO_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (sender is ComboBox cbMo && cbMo.SelectedItem != null)
            {
                string selectedMo = cbMo.SelectedItem.ToString();

                if (moModelMap.TryGetValue(selectedMo, out string modelName))
                {
                    SetTxtModelText(modelName);
                }
                else
                {
                    SetTxtModelText("");
                }
            }
            else
            {
                SetTxtModelText("");
            }
        }

        private void SetTxtModelText(string text)
        {
            Control[] foundModel = this.Controls.Find("txtModel", true);
            if (foundModel.Length > 0 && foundModel[0] is TextBox txtModel)
            {
                txtModel.Text = text;
            }
        }

        private void ClearMoAndModelControls()
        {
            moModelMap.Clear();

            Control[] foundMo = this.Controls.Find("cbMO", true);
            if (foundMo.Length > 0 && foundMo[0] is ComboBox cbMo)
            {
                cbMo.Items.Clear();
                cbMo.SelectedIndex = -1;
            }

            SetTxtModelText("");
        }

        #endregion

        #region PLC Sequence Step (Runner Engine & Sensor Integration)

        public class RecipeComboItem
        {
            public string RecipeId { get; set; }
            public string RecipeName { get; set; }

            public override string ToString()
            {
                return RecipeName;
            }
        }

        public class PlcActionModel
        {
            public string Address { get; set; }
            public bool State { get; set; }
        }

        public class SensorConfigModel
        {
            public string Source { get; set; } = "DMM";
            public string RegisterAddress { get; set; }
            public string Mode { get; set; }
            public string MinValue { get; set; }
            public string MaxValue { get; set; }
            public bool AllowOverload { get; set; }
            public string Unit { get; set; }
        }

        public class SequenceStepModel
        {
            public int StepNo { get; set; }
            public string Type { get; set; } = "CONTROL";
            public string Description { get; set; }
            public int DurationMs { get; set; }
            public List<PlcActionModel> Actions { get; set; } = new List<PlcActionModel>();
            public SensorConfigModel SensorConfig { get; set; }
        }

        public class SequenceRecipeModel
        {
            public string RecipeId { get; set; }
            public string RecipeName { get; set; }
            public List<SequenceStepModel> Steps { get; set; } = new List<SequenceStepModel>();
        }

        private async Task LoadStepRecipesAsync()
        {
            if (cbStep == null) return;

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    HttpResponseMessage response = await client.GetAsync($"{API_PLC_SEQUENCE_URL}/next-id");

                    if (response.IsSuccessStatusCode)
                    {
                        string json = await response.Content.ReadAsStringAsync();
                        using (JsonDocument doc = JsonDocument.Parse(json))
                        {
                            JsonElement root = doc.RootElement;
                            if (root.TryGetProperty("recipes", out JsonElement recipesArray) && recipesArray.ValueKind == JsonValueKind.Array)
                            {
                                cbStep.BeginUpdate();
                                cbStep.Items.Clear();

                                foreach (JsonElement r in recipesArray.EnumerateArray())
                                {
                                    string rId = r.GetProperty("RecipeId").GetString();
                                    string rName = r.GetProperty("RecipeName").GetString();

                                    cbStep.Items.Add(new RecipeComboItem
                                    {
                                        RecipeId = rId,
                                        RecipeName = rName
                                    });
                                }

                                cbStep.EndUpdate();

                                if (cbStep.Items.Count > 0)
                                {
                                    cbStep.SelectedIndex = 0;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                string detail = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                MessageBox.Show($"โหลดรายชื่อ Step จาก API ไม่สำเร็จ:\n{detail}", "API Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private async void btStepRefresh_Click(object sender, EventArgs e)
        {
            btStepRefresh.Enabled = false;
            try
            {
                await LoadStepRecipesAsync();
                MessageBox.Show("อัปเดตรายชื่อ Step สำเร็จ", "แจ้งเตือน", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            finally
            {
                btStepRefresh.Enabled = true;
            }
        }

        private async void btPLCStart_Click(object sender, EventArgs e)
        {
            if (isSequenceRunning)
            {
                MessageBox.Show("ระบบกำลังรัน Step อยู่แล้ว กรุณากด Stop ก่อนหากต้องการเริ่มใหม่", "แจ้งเตือน", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (tcpClient == null || !tcpClient.Connected || modbusMaster == null)
            {
                MessageBox.Show("กรุณาเชื่อมต่อ PLC ก่อนสั่งรัน Step", "แจ้งเตือน", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (cbStep.SelectedItem == null || !(cbStep.SelectedItem is RecipeComboItem selectedRecipe))
            {
                MessageBox.Show("กรุณาเลือก Step Sequence ที่ต้องการรันจาก ComboBox", "แจ้งเตือน", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string currentSn = "";
            Control[] foundBarcode = this.Controls.Find("txtBarcode", true);
            if (foundBarcode.Length > 0 && foundBarcode[0] is TextBox txtBarcode)
            {
                currentSn = txtBarcode.Text.Trim();
            }

            if (string.Equals(currentSn, "ERROR", StringComparison.OrdinalIgnoreCase))
            {
                UpdatePassFailLabel(false);
                UpdateSensorLabel("Barcode Error", Color.Red);

                await TriggerPlcM3030Async();
                return;
            }

            btPLCStart.Enabled = false;
            if (btPLCStop != null) btPLCStop.Enabled = true;

            if (dgvSensorResults != null)
            {
                dgvSensorResults.Rows.Clear();
            }

            Control[] foundLb = this.Controls.Find("lbPassFail", true);
            if (foundLb.Length > 0 && foundLb[0] is Label lbInit)
            {
                lbInit.Text = "TEST";
                lbInit.ForeColor = Color.White;
                lbInit.BackColor = ThemeWarning;
            }

            bool overallPass = true;
            bool hasResult = false;

            try
            {
                SequenceRecipeModel recipe = await FetchRecipeDetailAsync(selectedRecipe.RecipeId);
                if (recipe == null || recipe.Steps == null || recipe.Steps.Count == 0)
                {
                    MessageBox.Show($"ไม่พบข้อมูลขั้นตอนใน Recipe: {selectedRecipe.RecipeName}", "แจ้งเตือน", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                ctsSequence = new CancellationTokenSource();
                isSequenceRunning = true;

                await Task.Run(async () =>
                {
                    foreach (var step in recipe.Steps)
                    {
                        if (ctsSequence.Token.IsCancellationRequested) break;

                        if (string.Equals(step.Type, "READ_SENSOR", StringComparison.OrdinalIgnoreCase))
                        {
                            bool stepPass = await ExecuteSensorStepAsync(step, ctsSequence.Token);
                            if (!stepPass) overallPass = false;
                        }
                        else
                        {
                            foreach (var action in step.Actions)
                            {
                                if (TryParseDeltaAddress(action.Address, out ushort modbusAddress, out string detectedType))
                                {
                                    await modbusLock.WaitAsync(ctsSequence.Token);
                                    try
                                    {
                                        if (detectedType == "COIL")
                                        {
                                            await modbusMaster.WriteSingleCoilAsync(currentSlaveId, modbusAddress, action.State);
                                        }
                                        else if (detectedType == "REGISTER")
                                        {
                                            await modbusMaster.WriteSingleRegisterAsync(currentSlaveId, modbusAddress, (ushort)(action.State ? 1 : 0));
                                        }
                                    }
                                    finally
                                    {
                                        modbusLock.Release();
                                    }
                                }
                            }
                        }

                        if (step.DurationMs > 0)
                        {
                            await Task.Delay(step.DurationMs, ctsSequence.Token);
                        }
                    }
                }, ctsSequence.Token);

                if (!ctsSequence.Token.IsCancellationRequested)
                {
                    string jsonPayload = BuildUpdateRouteJson(currentSn, overallPass);

                    try
                    {
                        using (JsonDocument doc = JsonDocument.Parse(jsonPayload))
                        {
                            if (doc.RootElement.TryGetProperty("testData", out JsonElement testDataElem))
                            {
                                SaveTestDataLog(testDataElem.GetString());
                            }
                        }
                    }
                    catch { }

                    UpdatePassFailLabel(overallPass);
                    hasResult = true;

                    string mesMode = cbMES.SelectedItem?.ToString() ?? "MES";
                    bool isMesMode = string.Equals(mesMode, "MES", StringComparison.OrdinalIgnoreCase);

                    string popupMode = "Popup";
                    Control[] foundPopup = this.Controls.Find("cbPopup", true);
                    if (foundPopup.Length > 0 && foundPopup[0] is ComboBox cbPopupControl)
                    {
                        popupMode = cbPopupControl.SelectedItem?.ToString() ?? "Popup";
                    }
                    bool hasPopup = string.Equals(popupMode, "Popup", StringComparison.OrdinalIgnoreCase);

                    if (overallPass)
                    {
                        if (isMesMode)
                        {
                            await SendRoutingUpdateAsync(jsonPayload);
                        }

                        await TriggerPlcM3030Async();
                    }
                    else
                    {
                        if (!hasPopup)
                        {
                            if (isMesMode)
                            {
                                await SendRoutingUpdateAsync(jsonPayload);
                            }

                            await TriggerPlcM3030Async();
                        }
                        else
                        {
                            string confirmMsg = isMesMode
                                ? $"ผลการทดสอบ: FAIL (ชิ้นงานไม่ผ่านเกณฑ์)\nSN: {currentSn}\n\nต้องการส่งผล FAIL ไปยังระบบ MES หรือไม่?\n(กด OK เพื่อส่งผล FAIL / กด Cancel เพื่อไม่ส่งและทดสอบใหม่)"
                                : $"ผลการทดสอบ: FAIL (ชิ้นงานไม่ผ่านเกณฑ์)\nSN: {currentSn}\n\nโหมด No MES: กด OK หรือ Cancel เพื่อยืนยันรับทราบและปล่อยงาน";

                            DialogResult confirm = MessageBox.Show(
                                confirmMsg,
                                "Test Result: FAIL",
                                MessageBoxButtons.OKCancel,
                                MessageBoxIcon.Warning);

                            if (confirm == DialogResult.OK && isMesMode)
                            {
                                await SendRoutingUpdateAsync(jsonPayload);
                            }

                            await TriggerPlcM3030Async();
                        }
                    }
                }
            }
            // OperationCanceledException ครอบทั้ง Task.Delay และ SemaphoreSlim.WaitAsync ที่ถูกยกเลิก
            catch (OperationCanceledException)
            {
                MessageBox.Show("สั่งหยุดการทำงานของ Sequence เรียบร้อยแล้ว", "Stop", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"เกิดข้อผิดพลาดขณะรัน Step:\n{ex.Message}", "Execution Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                // ไม่ได้ผล PASS/FAIL (Recipe ว่าง / กด Stop / Error) → ไม่ให้ป้ายค้างที่ "TEST"
                if (!hasResult) SetPassFailReady();

                isSequenceRunning = false;
                btPLCStart.Enabled = true;
                if (ctsSequence != null)
                {
                    ctsSequence.Dispose();
                    ctsSequence = null;
                }
            }
        }

        private void btPLCStop_Click(object sender, EventArgs e)
        {
            if (isSequenceRunning && ctsSequence != null)
            {
                ctsSequence.Cancel();
            }
        }

        private async Task<SequenceRecipeModel> FetchRecipeDetailAsync(string recipeId)
        {
            using (HttpClient client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(10);
                HttpResponseMessage response = await client.GetAsync($"{API_PLC_SEQUENCE_URL}/get?recipeId={recipeId}");

                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    return JsonSerializer.Deserialize<SequenceRecipeModel>(json, options);
                }
            }
            return null;
        }

        private async Task<bool> ExecuteSensorStepAsync(SequenceStepModel step, CancellationToken token)
        {
            var cfg = step.SensorConfig;
            if (cfg == null) return true;

            DateTime startTime = DateTime.Now;
            string startStr = startTime.ToString("HH:mm:ss.ff");

            string stepTitle = !string.IsNullOrWhiteSpace(step.Description)
                ? step.Description.Trim()
                : $"Step #{step.StepNo}";

            string measuredStr = "";
            bool isPass = false;
            string specStr = BuildSpecString(cfg);

            bool isPlcRegister = string.Equals(cfg.Source, "PLC_REGISTER", StringComparison.OrdinalIgnoreCase);

            if (!isPlcRegister)
            {
                // ตรวจสอบสถานะการเชื่อมต่อ หากหลุดให้เปิดใหม่ทันที
                if (winUsbHandle == IntPtr.Zero && !EnsureDmmConnected())
                {
                    string endStr = DateTime.Now.ToString("HH:mm:ss.ff");
                    UpdateSensorLabel("Error", Color.Red);
                    AddResultToTable(stepTitle, specStr, "Disconn", false, startStr, endStr);
                    return false;
                }

                await dmmLock.WaitAsync(token);
                try
                {
                    // ล้าง Endpoint และบังคับให้อยู่ใน Remote Mode ผ่าน USB
                    WinUsb_AbortPipe(winUsbHandle, inPipeId);
                    WinUsb_AbortPipe(winUsbHandle, outPipeId);
                    SendUsbTmcCommand("SYST:REM\n");
                    SendUsbTmcCommand("*CLS\n");

                    string scpiMode = GetScpiCommandByMode(cfg.Mode);
                    SendUsbTmcCommand($"{scpiMode}\n");
                    Thread.Sleep(60);

                    SendUsbTmcCommand("TRIG:SOUR IMM\n");
                    SendUsbTmcCommand("TRIG:COUN 1\n");
                    SendUsbTmcCommand("SAMP:COUN 1\n");
                    Thread.Sleep(60);

                    SendUsbTmcCommand("READ?\n");
                    await Task.Delay(150, token);

                    string raw = ReadUsbTmcResponse();

                    // กรณีค้างจากการ Idle นาน: ทดลอง Reconnect ทันที 1 ครั้งแบบโปร่งใส
                    if (string.IsNullOrEmpty(raw))
                    {
                        CloseUsbHandles();
                        if (EnsureDmmConnected())
                        {
                            SendUsbTmcCommand("SYST:REM\n");
                            SendUsbTmcCommand($"{scpiMode}\n");
                            Thread.Sleep(60);
                            SendUsbTmcCommand("READ?\n");
                            await Task.Delay(150, token);
                            raw = ReadUsbTmcResponse();
                        }
                    }

                    if (!string.IsNullOrEmpty(raw) && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedVal))
                    {
                        string unit = (cfg.Unit ?? "").Replace("Ω", "").Trim();
                        bool isOverload = (parsedVal > 1e30 || parsedVal < -1e30);

                        if (isOverload)
                        {
                            measuredStr = string.IsNullOrEmpty(unit) ? "OL" : $"OL {unit}".Trim();
                            isPass = cfg.AllowOverload;
                        }
                        else
                        {
                            measuredStr = string.IsNullOrEmpty(unit) ? $"{parsedVal:F4}" : $"{parsedVal:F4} {unit}".Trim();
                            isPass = CheckPassCriteria(parsedVal, cfg.MinValue, cfg.MaxValue, cfg.AllowOverload);
                        }
                    }
                    else
                    {
                        measuredStr = string.IsNullOrEmpty(raw) ? "Error" : raw;
                        isPass = false;
                    }
                }
                catch (Exception)
                {
                    measuredStr = "Error";
                    isPass = false;
                }
                finally
                {
                    dmmLock.Release();
                }
            }
            else
            {
                string regAddr = cfg.RegisterAddress ?? "D100";
                if (TryParseDeltaAddress(regAddr, out ushort modbusAddr, out _))
                {
                    await modbusLock.WaitAsync(token);
                    try
                    {
                        ushort[] regs = await modbusMaster.ReadHoldingRegistersAsync(currentSlaveId, modbusAddr, 1);
                        double val = regs != null && regs.Length > 0 ? regs[0] : 0;

                        measuredStr = $"{val}";
                        isPass = CheckPassCriteria(val, cfg.MinValue, cfg.MaxValue, false);
                    }
                    finally
                    {
                        modbusLock.Release();
                    }
                }
            }

            string endStrComplete = DateTime.Now.ToString("HH:mm:ss.ff");

            UpdateSensorLabel(measuredStr, isPass ? Color.LimeGreen : Color.Red);
            AddResultToTable(stepTitle, specStr, measuredStr, isPass, startStr, endStrComplete);
            return isPass;
        }

        private bool CheckPassCriteria(double val, string minStr, string maxStr, bool allowOverload)
        {
            double min = 0;
            double max = 0;

            bool hasMin = !string.IsNullOrEmpty(minStr) &&
                          double.TryParse(minStr, NumberStyles.Float, CultureInfo.InvariantCulture, out min);

            bool hasMax = !string.IsNullOrEmpty(maxStr) &&
                          double.TryParse(maxStr, NumberStyles.Float, CultureInfo.InvariantCulture, out max);

            if (allowOverload && !hasMin && !hasMax)
            {
                return false;
            }

            if (hasMin && val < min) return false;
            if (hasMax && val > max) return false;

            return true;
        }

        private string BuildSpecString(SensorConfigModel cfg)
        {
            List<string> parts = new List<string>();
            string unit = (cfg.Unit ?? "").Replace("Ω", "").Trim();

            if (!string.IsNullOrEmpty(cfg.MinValue)) parts.Add($">={cfg.MinValue}");
            if (!string.IsNullOrEmpty(cfg.MaxValue)) parts.Add($"<={cfg.MaxValue}");
            if (cfg.AllowOverload) parts.Add("Allow OL");

            if (parts.Count == 0) return "No Limit";
            return $"{string.Join(" & ", parts)} {unit}".Trim();
        }

        private void AddResultToTable(object stepIdentifier, string spec, string measured, bool isOk, string startTime, string endTime)
        {
            if (dgvSensorResults == null) return;

            if (this.IsHandleCreated && !this.IsDisposed)
            {
                this.BeginInvoke(new Action(() =>
                {
                    int rowIndex = dgvSensorResults.Rows.Add(
                        stepIdentifier,
                        spec,
                        measured,
                        isOk ? "OK" : "NG",
                        startTime,
                        endTime
                    );

                    DataGridViewRow row = dgvSensorResults.Rows[rowIndex];

                    if (dgvSensorResults.Columns.Contains("colResult"))
                    {
                        var statusCell = row.Cells["colResult"];
                        statusCell.Style.Font = new Font(dgvSensorResults.Font, FontStyle.Bold);

                        if (isOk)
                        {
                            statusCell.Style.ForeColor = Color.Green;
                            row.DefaultCellStyle.BackColor = Color.FromArgb(240, 255, 240);
                        }
                        else
                        {
                            statusCell.Style.ForeColor = Color.Red;
                            row.DefaultCellStyle.BackColor = Color.FromArgb(255, 240, 240);
                        }
                    }

                    dgvSensorResults.FirstDisplayedScrollingRowIndex = rowIndex;
                }));
            }
        }

        private string GetScpiCommandByMode(string mode)
        {
            switch (mode?.ToUpper()?.Trim())
            {
                case "FRES": return "CONF:FRES";
                case "VOLT:DC": return "CONF:VOLT:DC";
                case "VOLT:AC": return "CONF:VOLT:AC";
                case "CURR:DC": return "CONF:CURR:DC";
                case "CURR:AC": return "CONF:CURR:AC";
                case "TEMP": return "CONF:TEMP";
                case "RES":
                default:
                    return "CONF:RES";
            }
        }

        private void UpdateSensorLabel(string text, Color color)
        {
            if (this.IsHandleCreated && !this.IsDisposed)
            {
                this.BeginInvoke(new Action(() =>
                {
                    Control[] found = this.Controls.Find("lbSensorValue", true);
                    if (found.Length > 0 && found[0] is Label lb)
                    {
                        lb.Text = text;
                        lb.ForeColor = color;
                    }
                }));
            }
        }

        #endregion

        #region USBTMC / WinUSB Communication (Picotest M352xA)

        private void StartDmmRealtimeMonitor()
        {
            if (isDmmMonitoring) return;

            ctsDmmMonitor = new CancellationTokenSource();
            isDmmMonitoring = true;

            Task.Run(() => DmmConnectionMonitorLoopAsync(ctsDmmMonitor.Token));
        }

        private void StopDmmRealtimeMonitor()
        {
            isDmmMonitoring = false;
            if (ctsDmmMonitor != null)
            {
                ctsDmmMonitor.Cancel();
                ctsDmmMonitor.Dispose();
                ctsDmmMonitor = null;
            }
        }

        private async Task DmmConnectionMonitorLoopAsync(CancellationToken token)
        {
            EnsureDmmConnected();

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(2000, token);

                    if (isSequenceRunning) continue;

                    string path = FindDevicePath(TARGET_VID, TARGET_PID);
                    bool isDevicePlugged = !string.IsNullOrEmpty(path);

                    if (!isDevicePlugged)
                    {
                        if (winUsbHandle != IntPtr.Zero)
                        {
                            CloseUsbHandles();
                        }
                        ShowDmmDisconnectAlert();
                    }
                    else
                    {
                        if (winUsbHandle == IntPtr.Zero && !isShowingDmmAlert)
                        {
                            EnsureDmmConnected();
                        }
                    }
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch
                {
                    await Task.Delay(2000, token);
                }
            }
        }

        private void ShowDmmDisconnectAlert()
        {
            if (isShowingDmmAlert) return;
            isShowingDmmAlert = true;

            if (this.IsHandleCreated && !this.IsDisposed)
            {
                this.BeginInvoke(new Action(() =>
                {
                    bool reconnected = false;

                    while (!reconnected && !this.IsDisposed)
                    {
                        DialogResult result = MessageBox.Show(
                            "ตรวจพบว่าการเชื่อมต่อกับ USB DMM (Picotest) หลุดออกจากระบบ!\n\nกรุณาเสียบสาย USB ให้แน่น แล้วกด [Retry] เพื่อเชื่อมต่อใหม่\n(หรือกด [Cancel] เพื่อปิดการแจ้งเตือนชั่วคราว)",
                            "USB DMM Disconnected",
                            MessageBoxButtons.RetryCancel,
                            MessageBoxIcon.Warning);

                        if (result == DialogResult.Retry)
                        {
                            if (EnsureDmmConnected())
                            {
                                reconnected = true;
                                MessageBox.Show("เชื่อมต่อกับ USB DMM สำเร็จเรียบร้อย!", "Connected", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }
                            else
                            {
                                MessageBox.Show("ยังไม่พบอุปกรณ์ USB DMM กรุณาตรวจสอบสายและพอร์ต USB อีกครั้ง", "Connection Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }
                        }
                        else
                        {
                            break;
                        }
                    }

                    isShowingDmmAlert = false;
                }));
            }
            else
            {
                isShowingDmmAlert = false;
            }
        }

        private void UpdateDmmStatusIndicator(bool isConnected)
        {
            if (this.IsHandleCreated && !this.IsDisposed)
            {
                this.BeginInvoke(new Action(() =>
                {
                    Control[] found = this.Controls.Find("pnThermistorConnection", true);
                    if (found.Length > 0 && found[0] is Panel pn)
                    {
                        pn.BackColor = isConnected ? Color.LimeGreen : Color.Crimson;
                    }
                }));
            }
        }

        private bool EnsureDmmConnected()
        {
            if (winUsbHandle != IntPtr.Zero)
            {
                UpdateDmmStatusIndicator(true);
                return true;
            }

            try
            {
                string devicePath = FindDevicePath(TARGET_VID, TARGET_PID);
                if (string.IsNullOrEmpty(devicePath))
                {
                    UpdateDmmStatusIndicator(false);
                    return false;
                }

                if (!OpenWinUsbDevice(devicePath))
                {
                    UpdateDmmStatusIndicator(false);
                    return false;
                }

                DetectPipes();

                // กำหนด Pipe Timeout 1.5 วินาที
                SetPipeTimeout(inPipeId, 1500);
                SetPipeTimeout(outPipeId, 1500);

                // สั่งล้าง Stall อัตโนมัติ (AUTO_CLEAR_STALL = 0x02)
                uint autoClear = 1;
                WinUsb_SetPipePolicy(winUsbHandle, inPipeId, 0x02, 4, ref autoClear);
                WinUsb_SetPipePolicy(winUsbHandle, outPipeId, 0x02, 4, ref autoClear);

                // ปิด Auto Suspend ระดับ WinUSB Driver ป้องกันระบบตัดไฟเมื่อไม่ได้ใช้งานนาน
                byte disableSuspend = 0;
                WinUsb_SetPowerPolicy(winUsbHandle, AUTO_SUSPEND, 1, ref disableSuspend);

                // ส่งคำสั่งล็อกให้อยู่ในโหมด USB Remote ตลอดไป
                SendUsbTmcCommand("SYST:REM\n");
                SendUsbTmcCommand("*CLS\n");

                UpdateDmmStatusIndicator(true);
                return true;
            }
            catch
            {
                CloseUsbHandles();
                UpdateDmmStatusIndicator(false);
                return false;
            }
        }

        private void SendUsbTmcCommand(string scpiCommand)
        {
            if (winUsbHandle == IntPtr.Zero) return;

            byte[] cmdBytes = Encoding.ASCII.GetBytes(scpiCommand);
            int transferSize = cmdBytes.Length;
            int padding = (4 - (transferSize % 4)) % 4;
            int totalPacketSize = 12 + transferSize + padding;

            byte[] packet = new byte[totalPacketSize];
            packet[0] = 1;
            packet[1] = usbTmcBTag;
            packet[2] = (byte)~usbTmcBTag;
            packet[3] = 0;
            packet[4] = (byte)(transferSize & 0xFF);
            packet[5] = (byte)((transferSize >> 8) & 0xFF);
            packet[6] = (byte)((transferSize >> 16) & 0xFF);
            packet[7] = (byte)((transferSize >> 24) & 0xFF);
            packet[8] = 1;
            packet[9] = 0;
            packet[10] = 0;
            packet[11] = 0;

            Array.Copy(cmdBytes, 0, packet, 12, transferSize);

            usbTmcBTag++;
            if (usbTmcBTag == 0) usbTmcBTag = 1;

            uint bytesWritten = 0;
            bool success = WinUsb_WritePipe(winUsbHandle, outPipeId, packet, (uint)packet.Length, ref bytesWritten, IntPtr.Zero);
            if (!success)
            {
                byte altPipe = (outPipeId == 0x02) ? (byte)0x01 : (byte)0x02;
                if (WinUsb_WritePipe(winUsbHandle, altPipe, packet, (uint)packet.Length, ref bytesWritten, IntPtr.Zero))
                {
                    outPipeId = altPipe;
                }
            }
        }

        private string ReadUsbTmcResponse()
        {
            if (winUsbHandle == IntPtr.Zero) return string.Empty;

            byte[] reqPacket = new byte[12];
            reqPacket[0] = 2;
            reqPacket[1] = usbTmcBTag;
            reqPacket[2] = (byte)~usbTmcBTag;
            reqPacket[3] = 0;
            reqPacket[4] = 0x00;
            reqPacket[5] = 0x04;
            reqPacket[6] = 0x00;
            reqPacket[7] = 0x00;
            reqPacket[8] = 0;
            reqPacket[9] = 0;
            reqPacket[10] = 0;
            reqPacket[11] = 0;

            usbTmcBTag++;
            if (usbTmcBTag == 0) usbTmcBTag = 1;

            uint bytesWritten = 0;
            if (!WinUsb_WritePipe(winUsbHandle, outPipeId, reqPacket, 12, ref bytesWritten, IntPtr.Zero))
                return string.Empty;

            byte[] readBuffer = new byte[1024];
            uint bytesRead = 0;
            if (WinUsb_ReadPipe(winUsbHandle, inPipeId, readBuffer, (uint)readBuffer.Length, ref bytesRead, IntPtr.Zero))
            {
                if (bytesRead >= 12 && readBuffer[0] == 2)
                {
                    int dataSize = BitConverter.ToInt32(readBuffer, 4);
                    int actualSize = Math.Min(dataSize, (int)bytesRead - 12);
                    if (actualSize > 0)
                    {
                        return Encoding.ASCII.GetString(readBuffer, 12, actualSize).Trim();
                    }
                }
                else if (bytesRead > 0)
                {
                    return Encoding.ASCII.GetString(readBuffer, 0, (int)bytesRead).Trim();
                }
            }

            return string.Empty;
        }

        private void DetectPipes()
        {
            inPipeId = 0x81;
            outPipeId = 0x02;

            try
            {
                USB_INTERFACE_DESCRIPTOR ifDesc = new USB_INTERFACE_DESCRIPTOR();
                if (WinUsb_QueryInterfaceSettings(winUsbHandle, 0, ref ifDesc))
                {
                    for (byte i = 0; i < ifDesc.bNumEndpoints; i++)
                    {
                        WINUSB_PIPE_INFORMATION pipeInfo = new WINUSB_PIPE_INFORMATION();
                        if (WinUsb_QueryPipe(winUsbHandle, 0, i, ref pipeInfo))
                        {
                            if ((pipeInfo.PipeId & 0x80) != 0)
                                inPipeId = pipeInfo.PipeId;
                            else
                                outPipeId = pipeInfo.PipeId;
                        }
                    }
                }
            }
            catch
            {
                inPipeId = 0x81;
                outPipeId = 0x02;
            }
        }

        private void SetPipeTimeout(byte pipeId, uint timeoutMs)
        {
            try
            {
                WinUsb_SetPipePolicy(winUsbHandle, pipeId, PIPE_TRANSFER_TIMEOUT, 4, ref timeoutMs);
            }
            catch { }
        }

        private void CloseUsbHandles()
        {
            if (winUsbHandle != IntPtr.Zero)
            {
                WinUsb_Free(winUsbHandle);
                winUsbHandle = IntPtr.Zero;
            }

            if (usbDeviceHandle != null && !usbDeviceHandle.IsInvalid)
            {
                usbDeviceHandle.Close();
                usbDeviceHandle.Dispose();
                usbDeviceHandle = null;
            }

            UpdateDmmStatusIndicator(false);
        }

        private bool OpenWinUsbDevice(string devicePath)
        {
            CloseUsbHandles();

            usbDeviceHandle = CreateFile(
                devicePath,
                GENERIC_READ | GENERIC_WRITE,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OVERLAPPED,
                IntPtr.Zero);

            if (usbDeviceHandle.IsInvalid) return false;

            bool initOk = WinUsb_Initialize(usbDeviceHandle, ref winUsbHandle);
            if (!initOk)
            {
                usbDeviceHandle.Close();
                usbDeviceHandle = null;
                return false;
            }

            return true;
        }

        private string FindDevicePath(ushort vid, ushort pid)
        {
            string target = $"vid_{vid:x4}&pid_{pid:x4}";

            foreach (Guid guid in TARGET_DEVICE_GUIDS)
            {
                Guid currentGuid = guid;
                IntPtr hDevInfo = SetupDiGetClassDevs(ref currentGuid, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
                if (hDevInfo.ToInt64() == -1) continue;

                try
                {
                    SP_DEVICE_INTERFACE_DATA ifData = new SP_DEVICE_INTERFACE_DATA();
                    ifData.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));

                    for (uint index = 0; SetupDiEnumDeviceInterfaces(hDevInfo, IntPtr.Zero, ref currentGuid, index, ref ifData); index++)
                    {
                        SetupDiGetDeviceInterfaceDetail(hDevInfo, ref ifData, IntPtr.Zero, 0, out uint requiredSize, IntPtr.Zero);
                        if (requiredSize == 0) continue;

                        IntPtr detailBuffer = Marshal.AllocHGlobal((int)requiredSize);
                        try
                        {
                            int cbSizeValue = (IntPtr.Size == 8) ? 8 : (4 + Marshal.SystemDefaultCharSize);
                            Marshal.WriteInt32(detailBuffer, 0, cbSizeValue);

                            if (SetupDiGetDeviceInterfaceDetail(hDevInfo, ref ifData, detailBuffer, requiredSize, out _, IntPtr.Zero))
                            {
                                IntPtr pDevicePath = new IntPtr(detailBuffer.ToInt64() + 4);
                                string path = Marshal.PtrToStringAuto(pDevicePath);

                                if (!string.IsNullOrEmpty(path) && path.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    return path;
                                }
                            }
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(detailBuffer);
                        }
                    }
                }
                finally
                {
                    SetupDiDestroyDeviceInfoList(hDevInfo);
                }
            }

            return null;
        }

        #region Native Win32 / WinUSB Declarations

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_SetPowerPolicy(IntPtr InterfaceHandle, uint PolicyType, uint ValueLength, ref byte Value);

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_ResetPipe(IntPtr InterfaceHandle, byte PipeID);

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_AbortPipe(IntPtr InterfaceHandle, byte PipeID);

        private const uint AUTO_SUSPEND = 0x81;

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_Initialize(SafeFileHandle DeviceHandle, ref IntPtr InterfaceHandle);

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_Free(IntPtr InterfaceHandle);

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_ReadPipe(IntPtr InterfaceHandle, byte PipeID, byte[] Buffer, uint BufferLength, ref uint LengthTransferred, IntPtr Overlapped);

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_WritePipe(IntPtr InterfaceHandle, byte PipeID, byte[] Buffer, uint BufferLength, ref uint LengthTransferred, IntPtr Overlapped);

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_QueryInterfaceSettings(IntPtr InterfaceHandle, byte AlternateInterfaceNumber, ref USB_INTERFACE_DESCRIPTOR UsbAltInterfaceDescriptor);

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_QueryPipe(IntPtr InterfaceHandle, byte AlternateInterfaceNumber, byte PipeIndex, ref WINUSB_PIPE_INFORMATION PipeInformation);

        [DllImport("winusb.dll", SetLastError = true)]
        private static extern bool WinUsb_SetPipePolicy(IntPtr InterfaceHandle, byte PipeID, uint PolicyType, uint ValueLength, ref uint Value);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, string Enumerator, IntPtr hwndParent, uint Flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr DeviceInfoSet, IntPtr DeviceInfoData, ref Guid InterfaceClassGuid, uint MemberIndex, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, IntPtr DeviceInterfaceDetailData, uint DeviceInterfaceDetailDataSize, out uint RequiredSize, IntPtr DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA
        {
            public uint cbSize;
            public Guid interfaceClassGuid;
            public uint flags;
            public IntPtr reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct USB_INTERFACE_DESCRIPTOR
        {
            public byte bLength;
            public byte bDescriptorType;
            public byte bInterfaceNumber;
            public byte bAlternateSetting;
            public byte bNumEndpoints;
            public byte bInterfaceClass;
            public byte bInterfaceSubClass;
            public byte bInterfaceProtocol;
            public byte iInterface;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINUSB_PIPE_INFORMATION
        {
            public int PipeType;
            public byte PipeId;
            public ushort MaximumPacketSize;
            public byte Interval;
        }

        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 1;
        private const uint FILE_SHARE_WRITE = 2;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
        private const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        private const uint DIGCF_PRESENT = 0x02;
        private const uint DIGCF_DEVICEINTERFACE = 0x10;
        private const uint PIPE_TRANSFER_TIMEOUT = 0x03;

        private static readonly Guid[] TARGET_DEVICE_GUIDS = new Guid[]
        {
            new Guid("A9FDBB24-112D-48BE-B084-22B9E2DE6886"),
            new Guid("A5DCBF10-6530-11D2-901F-00C04FB951ED"),
            new Guid("28E2744D-9826-4E74-B381-53C79A1745E0"),
            new Guid("4D36E978-E325-11CE-BFC1-08002BE10318"),
            new Guid("36FC9E60-C465-11CF-8056-444553540000")
        };

        #endregion

        #endregion

        #region Keyence Barcode Scanner (SR-X100W / TCP Data Port)

        private void LoadKeyenceSettings()
        {
            try
            {
                if (File.Exists(keyenceConfigFilePath))
                {
                    string[] lines = File.ReadAllLines(keyenceConfigFilePath);
                    if (lines.Length >= 2)
                    {
                        txtIPKeynce.Text = lines[0].Trim();
                        txtPortKeynce.Text = lines[1].Trim();
                    }
                }
            }
            catch { }

            // ค่า Default ของ Keyence SR-X100W หน้างาน: IP 192.168.1.99, Data Port 9004
            if (string.IsNullOrEmpty(txtIPKeynce.Text)) txtIPKeynce.Text = "192.168.1.99";
            if (string.IsNullOrEmpty(txtPortKeynce.Text)) txtPortKeynce.Text = "9004";
        }

        private void SaveKeyenceSettings()
        {
            try
            {
                File.WriteAllLines(keyenceConfigFilePath, new[] { txtIPKeynce.Text.Trim(), txtPortKeynce.Text.Trim() });
            }
            catch { }
        }

        private async void btConnectKeynce_Click(object sender, EventArgs e)
        {
            if (keyenceScanner != null && keyenceScanner.IsConnected)
            {
                DisconnectKeyence();
                MessageBox.Show("ตัดการเชื่อมต่อจาก Keyence Scanner เรียบร้อย", "Keyence Scanner", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            await ConnectKeyenceAsync(isSilent: false);
        }

        private async Task ConnectKeyenceAsync(bool isSilent)
        {
            if (isKeyenceConnecting) return;

            string ip = txtIPKeynce.Text.Trim();
            if (!IPAddress.TryParse(ip, out _) || !int.TryParse(txtPortKeynce.Text.Trim(), out int dataPort) || dataPort <= 0 || dataPort > 65535)
            {
                if (!isSilent)
                {
                    MessageBox.Show("กรุณากรอก IP และ Port ของ Keyence Scanner ให้ถูกต้อง", "Keyence Scanner", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return;
            }

            isKeyenceConnecting = true;
            btConnectKeynce.Enabled = false;
            btConnectKeynce.Text = "Connecting...";

            if (keyenceScanner == null)
            {
                keyenceScanner = new KeyenceScanner();
                keyenceScanner.BarcodeReceived += KeyenceScanner_BarcodeReceived;
                keyenceScanner.Disconnected += KeyenceScanner_Disconnected;
            }

            string error = null;
            try
            {
                await keyenceScanner.ConnectAsync(ip, dataPort);
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            finally
            {
                isKeyenceConnecting = false;
                btConnectKeynce.Enabled = true;
            }

            bool isConnected = error == null;
            UpdateKeyenceUi(isConnected);

            if (isConnected)
            {
                SaveKeyenceSettings();
                if (!isSilent)
                {
                    MessageBox.Show($"เชื่อมต่อ Keyence Scanner ({ip}:{dataPort}) สำเร็จ", "Keyence Scanner", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            else if (!isSilent)
            {
                MessageBox.Show($"เชื่อมต่อ Keyence Scanner ({ip}:{dataPort}) ไม่สำเร็จ:\n{error}", "Keyence Scanner", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void DisconnectKeyence()
        {
            try
            {
                keyenceScanner?.Disconnect();
            }
            catch { }
            UpdateKeyenceUi(false);
        }

        private void UpdateKeyenceUi(bool isConnected)
        {
            btConnectKeynce.Text = isConnected ? "Disconnect" : "Connect";
            btConnectKeynce.BackColor = isConnected ? ThemeSuccess : ThemePrimary;
            txtIPKeynce.Enabled = !isConnected;
            txtPortKeynce.Enabled = !isConnected;
        }

        // สาย LAN หลุด / Scanner ดับ — ถูกเรียกจาก Thread เบื้องหลัง
        private void KeyenceScanner_Disconnected()
        {
            if (!this.IsHandleCreated || this.IsDisposed) return;

            this.BeginInvoke(new Action(() => UpdateKeyenceUi(false)));
        }

        // ถูกเรียกจาก Thread เบื้องหลัง — ต้องส่งกลับไปทำงานบน UI Thread
        private void KeyenceScanner_BarcodeReceived(string barcode)
        {
            if (!this.IsHandleCreated || this.IsDisposed) return;

            // "ERROR" (อ่านไม่ออก) ส่งเข้า Flow เดียวกับ Scanner USB — btPLCStart_Click จะตั้ง FAIL และสั่ง M3030 ปล่อยชิ้นงาน
            this.BeginInvoke(new Action(async () => await ProcessScannedBarcodeAsync(barcode)));
        }

        #endregion

        #region Config Management (Load/Save)

        private void LoadModbusSettings()
        {
            try
            {
                if (File.Exists(configFilePath))
                {
                    string[] lines = File.ReadAllLines(configFilePath);
                    if (lines.Length >= 3)
                    {
                        txtIP.Text = lines[0].Trim();
                        txtPort.Text = lines[1].Trim();

                        string savedSlave = lines[2].Trim();
                        if (cbSlave.Items.Contains(savedSlave))
                            cbSlave.SelectedItem = savedSlave;
                        else
                            cbSlave.Text = savedSlave;
                        return;
                    }
                }
            }
            catch { }

            if (string.IsNullOrEmpty(txtIP.Text)) txtIP.Text = "192.168.1.30";
            if (string.IsNullOrEmpty(txtPort.Text)) txtPort.Text = "502";
            if (cbSlave.SelectedIndex == -1) cbSlave.SelectedIndex = 0;
        }

        private void SaveModbusSettings()
        {
            try
            {
                string slaveValue = cbSlave.SelectedItem != null ? cbSlave.SelectedItem.ToString() : cbSlave.Text;
                string[] dataToSave = new string[]
                {
                    txtIP.Text.Trim(),
                    txtPort.Text.Trim(),
                    slaveValue.Trim()
                };

                File.WriteAllLines(configFilePath, dataToSave);
            }
            catch { }
        }

        private void LoadMNAddresses()
        {
            try
            {
                if (File.Exists(mnConfigFilePath))
                {
                    string[] lines = File.ReadAllLines(mnConfigFilePath);

                    for (int i = 1; i <= 12; i++)
                    {
                        Control[] found = this.Controls.Find($"txtMN{i}", true);
                        if (found.Length > 0 && found[0] is TextBox txt)
                        {
                            int index = i - 1;
                            if (index < lines.Length)
                            {
                                txt.Text = lines[index].Trim();
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private void SaveMNAddresses()
        {
            try
            {
                List<string> addressList = new List<string>();

                for (int i = 1; i <= 12; i++)
                {
                    Control[] found = this.Controls.Find($"txtMN{i}", true);
                    if (found.Length > 0 && found[0] is TextBox txt)
                    {
                        addressList.Add(txt.Text.Trim());
                    }
                    else
                    {
                        addressList.Add("");
                    }
                }

                File.WriteAllLines(mnConfigFilePath, addressList);
            }
            catch { }
        }

        private void SaveLastSelectedLineAndStation()
        {
            try
            {
                string lastLine = cbLines.SelectedItem?.ToString() ?? "";
                string lastStation = cbStations.SelectedItem?.ToString() ?? "";

                if (!string.IsNullOrEmpty(lastLine))
                {
                    string[] data = new string[] { lastLine, lastStation };
                    File.WriteAllLines(lineStationConfigFilePath, data);
                }
            }
            catch { }
        }

        private void RestoreLastSelectedLineAndStation()
        {
            try
            {
                if (!File.Exists(lineStationConfigFilePath)) return;

                string[] lines = File.ReadAllLines(lineStationConfigFilePath);
                if (lines.Length >= 1)
                {
                    string savedLine = lines[0].Trim();
                    string savedStation = lines.Length >= 2 ? lines[1].Trim() : "";

                    if (!string.IsNullOrEmpty(savedLine) && cbLines.Items.Contains(savedLine))
                    {
                        cbLines.SelectedItem = savedLine;

                        if (!string.IsNullOrEmpty(savedStation) && cbStations.Items.Contains(savedStation))
                            cbStations.SelectedItem = savedStation;
                    }
                }
            }
            catch { }
        }

        #endregion

        #region Modbus TCP Connection & Real-time Polling

        private void UpdatePlcStatusIndicator(bool isConnected)
        {
            if (this.IsHandleCreated && !this.IsDisposed)
            {
                this.BeginInvoke(new Action(() =>
                {
                    Control[] found = this.Controls.Find("pnPLCConnection", true);
                    if (found.Length > 0 && found[0] is Panel pn)
                    {
                        pn.BackColor = isConnected ? Color.LimeGreen : Color.Crimson;
                    }
                }));
            }
        }

        private async Task<bool> ConnectPlcAsync(bool isSilent = false)
        {
            string ip = txtIP.Text.Trim();
            if (!IPAddress.TryParse(ip, out _))
            {
                if (!isSilent)
                {
                    MessageBox.Show("รูปแบบ IP Address ไม่ถูกต้อง กรุณากรอกใหม่ เช่น 192.168.1.30", "แจ้งเตือน", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    txtIP.Focus();
                }
                UpdatePlcStatusIndicator(false);
                return false;
            }

            if (!int.TryParse(txtPort.Text.Trim(), out int port) || port <= 0 || port > 65535)
            {
                if (!isSilent)
                {
                    MessageBox.Show("พอร์ตไม่ถูกต้อง (ค่ามาตรฐาน Modbus TCP คือ 502)", "แจ้งเตือน", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    txtPort.Focus();
                }
                UpdatePlcStatusIndicator(false);
                return false;
            }

            currentSlaveId = 1;
            string slaveStr = cbSlave.SelectedItem != null ? cbSlave.SelectedItem.ToString() : cbSlave.Text;
            if (!byte.TryParse(slaveStr.Trim(), out currentSlaveId) || currentSlaveId == 0)
            {
                currentSlaveId = 1;
            }

            btConnectPLC.Enabled = false;

            try
            {
                await Task.Run(() =>
                {
                    DisconnectPLC();

                    tcpClient = new TcpClient();
                    tcpClient.SendTimeout = 2000;
                    tcpClient.ReceiveTimeout = 2000;

                    tcpClient.Connect(ip, port);

                    var factory = new ModbusFactory();
                    modbusMaster = factory.CreateMaster(tcpClient);
                    modbusMaster.Transport.ReadTimeout = 2000;
                    modbusMaster.Transport.WriteTimeout = 2000;
                });

                if (tcpClient != null && tcpClient.Connected)
                {
                    SaveModbusSettings();
                    SaveMNAddresses();

                    btConnectPLC.Text = "Disconnect";
                    btConnectPLC.BackColor = ThemeSuccess;
                    SetPlcControlsEnabled(false);

                    UpdatePlcStatusIndicator(true);
                    StartRealtimePolling();

                    if (!isSilent)
                    {
                        MessageBox.Show($"เชื่อมต่อกับ PLC [{ip}:{port}] Slave ID: {currentSlaveId} สำเร็จ!", "สำเร็จ", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    return true;
                }
                else
                {
                    UpdatePlcStatusIndicator(false);
                    if (!isSilent)
                    {
                        MessageBox.Show("ไม่สามารถเชื่อมต่อ PLC ได้", "Connection Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    return false;
                }
            }
            catch (Exception ex)
            {
                DisconnectPLC();
                ResetAllPanelsToGray();
                UpdatePlcStatusIndicator(false);

                if (!isSilent)
                {
                    MessageBox.Show($"เกิดข้อผิดพลาดในการเชื่อมต่อ Modbus TCP:\n{ex.Message}", "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return false;
            }
            finally
            {
                btConnectPLC.Enabled = true;
            }
        }

        private async void btConnectPLC_Click(object sender, EventArgs e)
        {
            if (tcpClient != null && tcpClient.Connected)
            {
                StopRealtimePolling();
                if (isSequenceRunning && ctsSequence != null) ctsSequence.Cancel();
                DisconnectPLC();
                btConnectPLC.Text = "Connect";
                btConnectPLC.BackColor = ThemePrimary;
                SetPlcControlsEnabled(true);
                ResetAllPanelsToGray();
                UpdatePlcStatusIndicator(false);
                MessageBox.Show("ตัดการเชื่อมต่อจาก PLC เรียบร้อย", "Modbus TCP", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            await ConnectPlcAsync(isSilent: false);
        }

        private void StartRealtimePolling()
        {
            if (isPolling) return;

            ctsPolling = new CancellationTokenSource();
            isPolling = true;

            Task.Run(() => RealtimePollingLoopAsync(ctsPolling.Token));
        }

        private void StopRealtimePolling()
        {
            isPolling = false;
            if (ctsPolling != null)
            {
                ctsPolling.Cancel();
                ctsPolling.Dispose();
                ctsPolling = null;
            }
        }

        private async Task RealtimePollingLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && tcpClient != null && tcpClient.Connected)
            {
                try
                {
                    bool currentM3031 = false;
                    await modbusLock.WaitAsync(token);
                    try
                    {
                        bool[] m3031Coil = await modbusMaster.ReadCoilsAsync(currentSlaveId, 3031, 1);
                        currentM3031 = m3031Coil != null && m3031Coil.Length > 0 && m3031Coil[0];
                    }
                    catch { }
                    finally
                    {
                        modbusLock.Release();
                    }

                    if (!_prevM3031State && currentM3031)
                    {
                        if (!isSequenceRunning)
                        {
                            if (this.IsHandleCreated && !this.IsDisposed)
                            {
                                this.BeginInvoke(new Action(() =>
                                {
                                    btPLCStart_Click(this, EventArgs.Empty);
                                }));
                            }
                        }
                    }

                    _prevM3031State = currentM3031;

                    string[] currentAddresses = new string[12];
                    Array.Copy(cachedMnAddresses, currentAddresses, 12);

                    for (int i = 0; i < 12; i++)
                    {
                        if (token.IsCancellationRequested) break;

                        int panelIndex = i + 1;
                        string addressText = currentAddresses[i];

                        if (string.IsNullOrEmpty(addressText))
                        {
                            UpdatePanelColor(panelIndex, Color.Silver);
                            continue;
                        }

                        if (TryParseDeltaAddress(addressText, out ushort modbusAddress, out string detectedType))
                        {
                            try
                            {
                                bool isBitOn = false;

                                await modbusLock.WaitAsync(token);
                                try
                                {
                                    if (detectedType == "COIL")
                                    {
                                        bool[] coils = await modbusMaster.ReadCoilsAsync(currentSlaveId, modbusAddress, 1);
                                        isBitOn = coils != null && coils.Length > 0 && coils[0];
                                    }
                                    else if (detectedType == "INPUT_BIT")
                                    {
                                        bool[] inputs = await modbusMaster.ReadInputsAsync(currentSlaveId, modbusAddress, 1);
                                        isBitOn = inputs != null && inputs.Length > 0 && inputs[0];
                                    }
                                    else if (detectedType == "REGISTER")
                                    {
                                        ushort[] regs = await modbusMaster.ReadHoldingRegistersAsync(currentSlaveId, modbusAddress, 1);
                                        isBitOn = regs != null && regs.Length > 0 && regs[0] > 0;
                                    }
                                    else
                                    {
                                        try
                                        {
                                            ushort[] regs = await modbusMaster.ReadHoldingRegistersAsync(currentSlaveId, modbusAddress, 1);
                                            isBitOn = regs != null && regs.Length > 0 && regs[0] > 0;
                                        }
                                        catch
                                        {
                                            bool[] coils = await modbusMaster.ReadCoilsAsync(currentSlaveId, modbusAddress, 1);
                                            isBitOn = coils != null && coils.Length > 0 && coils[0];
                                        }
                                    }
                                }
                                finally
                                {
                                    modbusLock.Release();
                                }

                                UpdatePanelColor(panelIndex, isBitOn ? Color.LimeGreen : Color.Crimson);
                            }
                            catch
                            {
                                UpdatePanelColor(panelIndex, Color.Silver);
                            }
                        }
                        else
                        {
                            UpdatePanelColor(panelIndex, Color.Silver);
                        }
                    }

                    await Task.Delay(50, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    await Task.Delay(500, token);
                }
            }

            ResetAllPanelsToGray();
        }

        #endregion

        #region Control Buttons ON / OFF (btONMN1-12, btOFFMN1-12)

        private async void BtnONMN_Click(object sender, EventArgs e)
        {
            if (sender is Button btn)
            {
                string indexStr = btn.Name.Replace("btONMN", "");
                if (int.TryParse(indexStr, out int index))
                {
                    await SetPlcBitAsync(index, true);
                }
            }
        }

        private async void BtnOFFMN_Click(object sender, EventArgs e)
        {
            if (sender is Button btn)
            {
                string indexStr = btn.Name.Replace("btOFFMN", "");
                if (int.TryParse(indexStr, out int index))
                {
                    await SetPlcBitAsync(index, false);
                }
            }
        }

        private async Task SetPlcBitAsync(int index, bool state)
        {
            if (tcpClient == null || !tcpClient.Connected || modbusMaster == null)
            {
                MessageBox.Show("กรุณาเชื่อมต่อ PLC ก่อนสั่งงาน", "แจ้งเตือน", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string addressText = "";
            Control[] foundTxt = this.Controls.Find($"txtMN{index}", true);
            if (foundTxt.Length > 0 && foundTxt[0] is TextBox txt)
            {
                addressText = txt.Text.Trim();
            }

            if (string.IsNullOrEmpty(addressText))
            {
                MessageBox.Show($"กรุณาระบุ Address ในช่อง txtMN{index} ก่อนสั่งงาน", "แจ้งเตือน", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!TryParseDeltaAddress(addressText, out ushort modbusAddress, out string detectedType))
            {
                MessageBox.Show($"รูปแบบ Address '{addressText}' ไม่ถูกต้อง", "แจ้งเตือน", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (detectedType == "INPUT_BIT")
            {
                MessageBox.Show($"Address '{addressText}' เป็น Input (X) ไม่สามารถสั่ง Force ON/OFF ได้", "คำเตือน", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            await modbusLock.WaitAsync();
            try
            {
                if (detectedType == "REGISTER")
                {
                    ushort regVal = (ushort)(state ? 1 : 0);
                    await modbusMaster.WriteSingleRegisterAsync(currentSlaveId, modbusAddress, regVal);
                }
                else
                {
                    await modbusMaster.WriteSingleCoilAsync(currentSlaveId, modbusAddress, state);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"สั่งงานไม่สำเร็จ ({addressText}):\n{ex.Message}", "PLC Write Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                modbusLock.Release();
            }
        }

        #endregion

        #region Helpers & Address Parser

        private void SaveTestDataLog(string testDataLine)
        {
            try
            {
                DateTime now = DateTime.Now;
                string docPath = @"D:\";

                string targetDirectory = Path.Combine(
                    docPath,
                    "Thermistor",
                    now.ToString("yyyy"),
                    now.ToString("MM"),
                    now.ToString("dd")
                );

                if (!Directory.Exists(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                string logFilePath = Path.Combine(targetDirectory, "log.txt");

                File.AppendAllText(logFilePath, testDataLine + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ไม่สามารถบันทึก Log ไฟล์ได้: {ex.Message}");
            }
        }

        private async Task TriggerPlcM3030Async()
        {
            if (tcpClient != null && tcpClient.Connected && modbusMaster != null)
            {
                await modbusLock.WaitAsync();
                try
                {
                    await modbusMaster.WriteSingleCoilAsync(currentSlaveId, 3030, true);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ไม่สามารถสั่ง Trigger M3030 ได้: {ex.Message}");
                }
                finally
                {
                    modbusLock.Release();
                }
            }
        }

        private Size _originalFormSize;
        private readonly Dictionary<Control, (Rectangle Bounds, float FontSize)> _controlsOriginalState
            = new Dictionary<Control, (Rectangle Bounds, float FontSize)>();

        private void RecordInitialControlStates(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                if (!_controlsOriginalState.ContainsKey(c))
                {
                    _controlsOriginalState[c] = (c.Bounds, c.Font.Size);
                }
                if (c.HasChildren)
                {
                    RecordInitialControlStates(c);
                }
            }
        }

        private void Form2_Resize(object sender, EventArgs e)
        {
            if (this.WindowState == FormWindowState.Minimized) return;
            if (_originalFormSize.Width == 0 || _originalFormSize.Height == 0) return;

            float scaleX = (float)this.ClientSize.Width / _originalFormSize.Width;
            float scaleY = (float)this.ClientSize.Height / _originalFormSize.Height;
            float fontScale = (scaleY * 0.6f) + (scaleX * 0.4f);

            this.SuspendLayout();
            ResizeAllControls(this, scaleX, scaleY, fontScale);
            this.ResumeLayout();
        }

        private void ResizeAllControls(Control parent, float scaleX, float scaleY, float fontScale)
        {
            foreach (Control c in parent.Controls)
            {
                if (_controlsOriginalState.TryGetValue(c, out var orig))
                {
                    int newX = (int)Math.Round(orig.Bounds.X * scaleX);
                    int newY = (int)Math.Round(orig.Bounds.Y * scaleY);
                    int newW = (int)Math.Round(orig.Bounds.Width * scaleX);
                    int newH = (int)Math.Round(orig.Bounds.Height * scaleY);

                    if (c.Name == "pnPLCConnection" || c.Name == "pnThermistorConnection")
                    {
                        int squareSide = Math.Min(newW, newH);
                        newW = squareSide;
                        newH = squareSide;
                    }

                    c.SetBounds(newX, newY, newW, newH);

                    // lbPassFail คำนวณฟอนต์เองใน FitPassFailFont() ตอน SizeChanged
                    if (fontScale > 0 && c != lbPassFail)
                    {
                        float newFontSize = Math.Max(8f, orig.FontSize * fontScale);
                        c.Font = new Font(c.Font.FontFamily, newFontSize, c.Font.Style);
                    }

                    if (c is DataGridView dgv && dgv.Rows.Count > 0)
                    {
                        int baseRowHeight = 24;
                        dgv.RowTemplate.Height = Math.Max(24, (int)(baseRowHeight * scaleY));
                        foreach (DataGridViewRow row in dgv.Rows)
                        {
                            row.Height = dgv.RowTemplate.Height;
                        }
                    }
                }

                if (c.HasChildren)
                {
                    ResizeAllControls(c, scaleX, scaleY, fontScale);
                }
            }
        }

        private async Task<bool> SendRoutingUpdateAsync(string jsonPayload)
        {
            string url = $"{API_MES_BASE_URL}/tdc/typed/routing-update";

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                    HttpResponseMessage response = await client.PostAsync(url, content);
                    string resContent = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        return true;
                    }
                    else
                    {
                        MessageBox.Show($"Update Route ล้มเหลว (HTTP {(int)response.StatusCode}):\n{resContent}",
                                        "MES Update Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"เกิดข้อผิดพลาดในการส่งข้อมูล Update Route:\n{ex.Message}",
                                "MES Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private void UpdatePassFailLabel(bool isPass)
        {
            if (this.IsHandleCreated && !this.IsDisposed)
            {
                this.BeginInvoke(new Action(() =>
                {
                    Control[] found = this.Controls.Find("lbPassFail", true);
                    if (found.Length > 0 && found[0] is Label lb)
                    {
                        lb.Text = isPass ? "PASS" : "FAIL";
                        lb.ForeColor = Color.White;
                        lb.BackColor = isPass ? ThemeSuccess : ThemeDanger;
                    }
                }));
            }
        }

        private bool TryParseDeltaAddress(string input, out ushort address, out string detectedType)
        {
            address = 0;
            detectedType = "AUTO";

            if (string.IsNullOrEmpty(input)) return false;
            input = input.Trim().ToUpper();

            if (input.StartsWith("Y"))
            {
                if (input.Contains("."))
                {
                    string[] parts = input.Substring(1).Split('.');
                    if (parts.Length == 2 && ushort.TryParse(parts[0], out ushort word) && ushort.TryParse(parts[1], out ushort bit))
                    {
                        address = (ushort)(40960 + (word * 16) + bit);
                        detectedType = "COIL";
                        return true;
                    }
                }
                else if (ushort.TryParse(input.Substring(1), out ushort yNum))
                {
                    address = (ushort)(40960 + yNum);
                    detectedType = "COIL";
                    return true;
                }
            }
            else if (input.StartsWith("X"))
            {
                if (input.Contains("."))
                {
                    string[] parts = input.Substring(1).Split('.');
                    if (parts.Length == 2 && ushort.TryParse(parts[0], out ushort word) && ushort.TryParse(parts[1], out ushort bit))
                    {
                        address = (ushort)(24576 + (word * 16) + bit);
                        detectedType = "INPUT_BIT";
                        return true;
                    }
                }
                else if (ushort.TryParse(input.Substring(1), out ushort xNum))
                {
                    address = (ushort)(24576 + xNum);
                    detectedType = "INPUT_BIT";
                    return true;
                }
            }
            else if (input.StartsWith("M"))
            {
                if (ushort.TryParse(input.Substring(1), out ushort mNum))
                {
                    address = mNum;
                    detectedType = "COIL";
                    return true;
                }
            }
            else if (input.StartsWith("D"))
            {
                if (ushort.TryParse(input.Substring(1), out ushort dNum))
                {
                    address = dNum;
                    detectedType = "REGISTER";
                    return true;
                }
            }
            else if (ushort.TryParse(input, out ushort numAddr))
            {
                address = numAddr;
                if (address >= 40960 && address <= 42000)
                {
                    detectedType = "COIL";
                }
                else
                {
                    detectedType = "AUTO";
                }
                return true;
            }

            return false;
        }

        private void UpdatePanelColor(int index, Color color)
        {
            if (this.IsHandleCreated && !this.IsDisposed)
            {
                this.BeginInvoke(new Action(() =>
                {
                    Control[] found = this.Controls.Find($"pnMN{index}", true);
                    if (found.Length > 0 && found[0] is Panel pn)
                    {
                        if (pn.BackColor != color)
                        {
                            pn.BackColor = color;
                        }
                    }
                }));
            }
        }

        private void ResetAllPanelsToGray()
        {
            for (int i = 1; i <= 12; i++)
            {
                UpdatePanelColor(i, Color.Silver);
            }
        }

        private void DisconnectPLC()
        {
            try
            {
                if (modbusMaster != null)
                {
                    modbusMaster.Dispose();
                    modbusMaster = null;
                }
                if (tcpClient != null)
                {
                    tcpClient.Close();
                    tcpClient = null;
                }
            }
            catch { }
        }

        private void SetPlcControlsEnabled(bool enabled)
        {
            txtIP.Enabled = enabled;
            txtPort.Enabled = enabled;
            cbSlave.Enabled = enabled;
        }

        private void Form2_FormClosed(object sender, FormClosedEventArgs e)
        {
            StopDmmRealtimeMonitor();
            StopRealtimePolling();
            if (isSequenceRunning && ctsSequence != null) ctsSequence.Cancel();
            ResetAllPanelsToGray();
            SaveModbusSettings();
            SaveMNAddresses();
            SaveLastSelectedLineAndStation();
            DisconnectPLC();
            UpdatePlcStatusIndicator(false);
            SaveKeyenceSettings();
            DisconnectKeyence();
            CloseUsbHandles();
            Application.Exit();
        }

        #endregion
    }
}
