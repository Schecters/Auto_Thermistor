using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Themister_Real
{
    public partial class Login_Form : Form
    {
        public Login_Form()
        {
            InitializeComponent();
        }

        private async void buttonLogin_Click(object sender, EventArgs e)
        {
            string empId = txtEMID.Text.Trim();
            string password = txtPassword.Text.Trim();

            if (string.IsNullOrEmpty(empId) || string.IsNullOrEmpty(password))
            {
                MessageBox.Show("กรุณากรอก Employee ID และ Password ให้ครบถ้วน", "แจ้งเตือน", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            buttonLogin.Enabled = false;

            try
            {
                string jsonPayload = JsonSerializer.Serialize(new { employeeId = empId, password = password, deviceId = "WinForms_Client" });
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(10);
                    string apiUrl = "http://10.204.206.1:5000/api/login";

                    HttpResponseMessage response = await client.PostAsync(apiUrl, content);
                    string responseBody = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        var roleMatch = Regex.Match(responseBody, "\"role\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
                        var nameMatch = Regex.Match(responseBody, "\"name\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
                        var tokenMatch = Regex.Match(responseBody, "\"token\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);

                        string role = roleMatch.Success ? roleMatch.Groups[1].Value.ToUpper().Trim() : "";
                        string name = nameMatch.Success ? nameMatch.Groups[1].Value : empId;
                        string token = tokenMatch.Success ? tokenMatch.Groups[1].Value : "";

                        if (role == "ME" || role == "ADMIN")
                        {
                            // บันทึกค่าลงตัวแปร Session
                            UserSession.EmployeeId = empId;
                            UserSession.Name = name;
                            UserSession.Role = role;
                            UserSession.Token = token;

                            MessageBox.Show($"ยินดีต้อนรับคุณ {name}\nเข้าสู่ระบบด้วยสิทธิ์: {role}", "เข้าสู่ระบบสำเร็จ", MessageBoxButtons.OK, MessageBoxIcon.Information);

                            Form2 f2 = new Form2();
                            f2.Show();

                            this.Hide();
                        }
                        else
                        {
                            MessageBox.Show($"บัญชีนี้มีสิทธิ์ '{role}' ไม่สามารถเข้าใช้งานระบบนี้ได้", "สิทธิ์ไม่เพียงพอ", MessageBoxButtons.OK, MessageBoxIcon.Stop);
                        }
                    }
                    else
                    {
                        var msgMatch = Regex.Match(responseBody, "\"message\"\\s*:\\s*\"([^\"]+)\"");
                        string errorMessage = msgMatch.Success ? msgMatch.Groups[1].Value : $"HTTP {(int)response.StatusCode}";

                        MessageBox.Show(errorMessage, "Login Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"เกิดข้อผิดพลาดในการเชื่อมต่อ Server (10.204.206.1:5000):\n{ex.Message}", "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                buttonLogin.Enabled = true;
            }
        }
    }

    // ประกาศคลาส UserSession ให้อยู่ใต้ namespace เดียวกัน
    public static class UserSession
    {
        public static string EmployeeId { get; set; } = "";
        public static string Name { get; set; } = "";
        public static string Role { get; set; } = "";
        public static string Token { get; set; } = "";

        public static void Clear()
        {
            EmployeeId = "";
            Name = "";
            Role = "";
            Token = "";
        }
    }
}