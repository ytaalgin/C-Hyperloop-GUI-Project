using LibVLCSharp.Shared;
using Microsoft.Office.Interop.Excel; //logları excel dosyası olarak kaydetmek için kullanıldı
using Newtonsoft.Json;
using SimpleTCP;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;
using Syncfusion.Windows.Forms.Gauge; //Syncfusion'un gerçek zamanlı gösterge kütüphanesi
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO; //excel com file ve path bilgilerinin çekilmesi için kullanıldı
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting; //Windows'un gerçek zamanlı grafik kütüphanesi
using Action = System.Action;
using Message = SimpleTCP.Message;

namespace Modern_Sliding_Sidebar___C_Sharp_Winform
{
    public partial class Form1 : Form
    {
        public LibVLC _libVLC;
        public MediaPlayer _mp;
        public Media media;

        public Size oldVideoSize;
        public Size oldFormSize;
        public System.Windows.Point oldVideoLocation;

        bool sideBar_Expand = true; //sidebar için yapılandırma ayarları

        private List<string> notifications; //ana ekrandaki uyarı alanı bildirimlerini tanımla
        private int currentNotificationIndex;

        private List<string[]> pendingLogData; //excel verilerini tut

        private float previousXAcceleration = 0; // Önceki x ivmesini saklamak için
        private float previousYAcceleration = 0; // Önceki y ivmesini saklamak için
        private float previousZAcceleration = 0; // Önceki z ivmesini saklamak için

        private int elapsedTimeInSeconds = 0; //zaman göstergesinde saniyeleri tanımla

        // Form veya sınıf düzeyinde bayrak tanımları
        private bool lowBatteryWarningShown = false;

        private Guna.UI.WinForms.GunaButton selectedButton;

        private float startTime = 0;
        private float startCharge = 100;

        private SensorData sensorData;

        //video kaydı için
        private bool _recording;
        private Media _media;

        SimpleTcpClient client;
        public Form1()
        {
            //syncfusion community lisansı için lisans anahtarını gir
            Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense("Mjk5NjA2OUAzMjM0MmUzMDJlMzBUTEZqZmYxT29sUUhtbVJqZytPeGNmWVVOTzF0TjVlak51Mk9GNWhNRGFvPQ==");

            InitializeComponent(); //form içeriğini göster
            InitializeDataGridView(); //logları göstermek için datagrid ile tablo oluştur

            Core.Initialize();
            oldVideoSize = videoView1.Size;
            oldFormSize = this.Size;

            //VLC stuff
            _libVLC = new LibVLC();
            _mp = new MediaPlayer(_libVLC);
            videoView1.MediaPlayer = _mp;

            selectedButton = Orders_Button;
            SetButtonColor(selectedButton);

            timerChartUpdate.Interval = 1000; // saniyede bir grafiği güncelle (bu değer ihtiyaca göre değiştirilebilir)
            timerChartUpdate.Tick += TimerChartUpdate_Tick; //grafik güncelleyici timer'ı tetikle

            pendingLogData = new List<string[]>();

            notifications = new List<string> //bildirimleri şimdilik böyle tanımla. Daha sonra bu kısım yapılandırılacak!!! (Örnek: Son 100 Metre)
            {
                "Bu bir uyarı mesajıdır.",
                "Başka bir uyarı mesajı.",
                "Bir başka önemli uyarı."
                // Uyarılar çoğaltılabilir.
            };

            currentNotificationIndex = 0;

            timer1.Interval = 10000; // 10 saniyede bir ana ekrandaki uyarıları değiştir
            timer1.Start(); //ana ekrandaki uyarıları değiştiren timer'ı başlat

            tabPage1.Text = ""; //tabcontrol1 tabpage'de üstteki buton yazılarını sil
            tabPage2.Text = "";
            tabPage3.Text = "";
            tabPage4.Text = "";
            tabPage5.Text = "";
            tabPage6.Text = "";

            tabControl1.ItemSize = new Size(1, 1); //tabcontroldeki butonları yok et

            tabControl2.DrawMode = TabDrawMode.OwnerDrawFixed; // Tabları özel çizim moduna geçir
            tabControl2.DrawItem += TabControl2_DrawItem; // DrawItem olayına bağlan
        }

        private void InitializeDataGridView() //log tablosunu yapılandır
        {
            // DataGridView'e iki sütun ekle
            dataGridViewLogs.Columns.Add("DateTime", "Tarih");
            dataGridViewLogs.Columns.Add("Status", "Durum");

            // DataGridView'in düzenini ayarla
            dataGridViewLogs.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dataGridViewLogs.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
        }

        private void timer1_Tick(object sender, EventArgs e) //ana ekrandaki uyarı alanı timer_tick olayını ayarla
        {
            currentNotificationIndex = (currentNotificationIndex + 1) % notifications.Count;
            label5.Text = notifications[currentNotificationIndex];
        }

        private void btnUpdate_Click(object sender, EventArgs e) //logları güncelle
        {
            // Güncelle butonuna tıklandığında DataGridView'e veri ekleyin
            string dateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string status = "Manuel olarak güncelleme yapıldı!";

            // DataGridView'e yeni satır ekle
            dataGridViewLogs.Rows.Insert(0, dateTime, status);

            // En üstteki satıra kaydır
            dataGridViewLogs.FirstDisplayedScrollingRowIndex = 0;

            // Excel'e veriyi eklemek için listeye ekle
            pendingLogData.Add(new string[] { dateTime, status });

            // Excel'e veriyi işle
            ProcessPendingLogData();
        }

        private void ProcessPendingLogData()
        {
            try
            {
                // Excel dosyasının adını belirle
                string excelFileName = "loglar.xlsx";

                // Excel dosyasının yolu
                string excelFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), excelFileName);

                Microsoft.Office.Interop.Excel.Application excelApp = new Microsoft.Office.Interop.Excel.Application();
                Workbook excelWorkbook;

                if (File.Exists(excelFilePath))
                {
                    // Dosya zaten varsa, var olan dosyayı aç
                    excelWorkbook = excelApp.Workbooks.Open(excelFilePath);
                }
                else
                {
                    // Dosya yoksa, yeni bir dosya oluştur
                    excelWorkbook = excelApp.Workbooks.Add();
                    InitializeExcelHeader(excelWorkbook); // Başlık eklemek için ayrı bir metod kullanıldı
                    excelWorkbook.SaveAs(excelFilePath);
                }

                Worksheet excelWorksheet = (Worksheet)excelWorkbook.ActiveSheet;

                // Excel'e veriyi eklemek için
                int rowIndex = excelWorksheet.Cells.SpecialCells(XlCellType.xlCellTypeLastCell).Row + 1;

                foreach (var logData in pendingLogData)
                {
                    string dateTime = logData[0];
                    string status = logData[1];

                    // Hücrelere değerleri ekleyin
                    excelWorksheet.Cells[rowIndex, 1] = dateTime;
                    excelWorksheet.Cells[rowIndex, 2] = status;

                    rowIndex++;
                }

                // Excel dosyasını kaydet
                excelWorkbook.Save();
                excelApp.Quit();

                // İşlenen verileri temizle
                pendingLogData.Clear();
            }
            catch (Exception ex)
            {
                LogError($"Excel'e kaydedilirken bilinmeyen bir hata oluştu. Hata Detayı: {ex.Message}\nStack Trace: {ex.StackTrace}");
            }
        }

        // InitializeExcelHeader metodu - Excel'e başlık ekle
        private void InitializeExcelHeader(Workbook excelWorkbook)
        {
            // Başlık ekleme işlemleri burada gerçekleştirilir
            // Örnek olarak sadece başlık adları eklenmiştir
            // İhtiyaca göre başlıklar ve stil ayarları değiştirilebilir
            Worksheet excelWorksheet = (Worksheet)excelWorkbook.ActiveSheet;
            excelWorksheet.Cells[1, 1] = "Tarih";
            excelWorksheet.Cells[1, 2] = "Durum";
        }

        private void btnClear_Click(object sender, EventArgs e)
        {
            // Kayıtları sil butonuna tıklandığında logları temizle
            dataGridViewLogs.Rows.Clear();
        }

        private void StartRecordingButton_Click(object sender, EventArgs e)
        {
            if (!_recording)
            {
                // Kayıt başlat
                StartRecording();

                _recording = true;
            }
        }

        private void StopRecordingButton_Click(object sender, EventArgs e)
        {
            if (_recording)
            {
                // Kayıt durdur
                StopRecording();

                _recording = false;
            }
        }

        private void StartButton_Click(object sender, EventArgs e) //kamerayı başlatmak için start butonu
        {
            string cameraUrl = "http://172.27.42.77:8080/video";
            _media = new Media(_libVLC, new Uri(cameraUrl));
            _mp.Play(_media);
        }

        private void StopButton_Click(object sender, EventArgs e) //kamerayı durdurmak için stop butonu
        {
            _mp.Stop();

            // Kaydı durdur (eğer kaydediliyorsa)
            StopRecording();
        }

        private void StartRecording()
        {
            try
            {
                // Eğer _media null değilse ve kayıt başlamamışsa
                if (_media != null && !_recording)
                {
                    // Kayıt dosyasının yolu ve adı
                    string outputFilePath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop) + "\\output.mp4";

                    // Yeni bir Media oluştur
                    _media = new Media(_libVLC, new Uri("http://172.27.42.77:8080/video"));

                    // Kayıt seçeneklerini ayarla ve başlat
                    _media.AddOption(":sout=#transcode{vcodec=h264}:file{dst=" + outputFilePath + "}");

                    _recording = true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Kayıt başlatılırken hata oluştu: " + ex.Message);
                // Hata mesajını uygun bir şekilde işleyebilirsiniz.
            }
        }

        private void StopRecording()
        {
            // Eğer kayıt başlamışsa
            if (_recording)
            {
                // Kaydı temizle
                _media?.Dispose();
                _media = null;

                _recording = false;
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            _mp.Stop();
            _mp.Dispose();
            _libVLC.Dispose();

            // Timer'ı durdur
            timerChartUpdate.Stop();
        }

        private void TabControl2_DrawItem(object sender, DrawItemEventArgs e) //batarya yönetim sistemi tabcontrolünü yapılandır
        {
            // Seçilen tabın arkaplan rengini belirle (Sadece başlık arkaplanı)
            Color selectedTabColor = Color.DodgerBlue;
            // Metin rengini belirle
            Color textColor = Color.White;

            TabPage tabPage = tabControl2.TabPages[e.Index];

            // Başlık arkaplanını boya
            using (SolidBrush brush = new SolidBrush(selectedTabColor))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }

            // Metni çiz
            using (SolidBrush brush = new SolidBrush(textColor))
            {
                StringFormat sf = new StringFormat();
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;

                e.Graphics.DrawString(tabPage.Text, tabPage.Font, brush, e.Bounds, sf);
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            //bu kısma bir şeyler eklenebilir
        }

        private void gunaPanel1_Paint(object sender, PaintEventArgs e)
        {
            //bu kısma bir şeyler eklenebilir
        }
        private void Close_Button_Click(object sender, EventArgs e)
        {
            System.Windows.Forms.Application.Exit(); //close butonuna basınca programdan çıkış yap
        }

        private void Timer_Sidebar_Menu_Tick(object sender, EventArgs e) //sidebar (menu bar) kapatıldığında transition süresini ayarla
        {
            if (sideBar_Expand)
            {
                SideBar.Width -= 10;
                if (SideBar.Width == SideBar.MinimumSize.Width)
                {
                    sideBar_Expand = false;
                    Timer_Sidebar_Menu.Stop();
                }
            }
            else
            {
                SideBar.Width += 10;
                if (SideBar.Width == SideBar.MaximumSize.Width)
                {
                    sideBar_Expand = true;
                    Timer_Sidebar_Menu.Stop();
                }
            }
        }

        private void Menu_Button_Click(object sender, EventArgs e) //menu butonuna basınca menu timer'ını tetikle
        {
            Timer_Sidebar_Menu.Start(); //bu ayar menünün bir anda kapanmaması içindir
        }

        private void SetButtonColor(Guna.UI.WinForms.GunaButton button)
        {
            // Seçili butonun rengini değiştir
            button.BackColor = Color.DodgerBlue;

            // Daha önce seçilen butonun rengini eski haline getir
            if (selectedButton != null && selectedButton != button)
            {
                selectedButton.BackColor = Color.Transparent; // Varsayılan arka plan rengi
            }

            // Yeni seçili butonu sakla
            selectedButton = button;
        }

        private void ChangeTabAndColor(Guna.UI.WinForms.GunaButton button, TabPage tabPage)
        {
            // Tab Page değişikliği
            tabControl1.SelectedTab = tabPage;

            // Buton rengi güncelleme
            SetButtonColor(button);
        }

        private void Home_Button_Click(object sender, EventArgs e)
        {
            ChangeTabAndColor(Home_Button, tabPage2); //bağlantı ekranını aç
        }

        private void pictureBox1_Click(object sender, EventArgs e)
        {
            ChangeTabAndColor(Orders_Button, tabPage1); //ana ekranı aç
        }

        private void Orders_Button_Click(object sender, EventArgs e)
        {
            ChangeTabAndColor(Orders_Button, tabPage1); //ana ekranı aç
        }

        private void Customers_Button_Click(object sender, EventArgs e)
        {
            ChangeTabAndColor(Customers_Button, tabPage3); //basınç ve sıcaklık ekranını aç
        }

        private void Statistics_Button_Click(object sender, EventArgs e)
        {
            ChangeTabAndColor(Statistics_Button, tabPage4); //batarya yönetim sistemi ekranını aç
        }

        private void About_Button_Click(object sender, EventArgs e)
        {
            ChangeTabAndColor(About_Button, tabPage5); //IPCAM ekranını aç
        }

        private void Help_Button_Click(object sender, EventArgs e)
        {
            ChangeTabAndColor(Help_Button, tabPage6); //Log analizi ekranını aç
        }

        private void label1_Click(object sender, EventArgs e) //ekranı label1 ile minizime et
        {
            WindowState = FormWindowState.Minimized;
        }

        // button6_Click metodu: butona basıp serial portu başlat
        private void button6_Click(object sender, EventArgs e)
        {
            try
            {
                // IP Adresi Kontrolü
                string ipAddress = textHost.Text;
                if (!System.Net.IPAddress.TryParse(ipAddress, out _))
                {
                    MessageBox.Show("Geçersiz IP adresi.");
                    return; // Geçersiz IP adresi durumunda metodu terk et
                }

                // Bağlantı Noktası Kontrolü
                int port;
                if (!int.TryParse(textPort.Text, out port) || port < 0 || port > 65535)
                {
                    MessageBox.Show("Geçersiz bağlantı noktası.");
                    return; // Geçersiz bağlantı noktası durumunda metodu terk et
                }

                // IP adresi ve bağlantı noktası geçerliyse TCP sunucuyu başlat
                client = new SimpleTcpClient();
                client.Connect(ipAddress, port);  // Connect metoduna string tipinde IP adresi ver
                client.DataReceived += Client_DataReceived; // Veri alındığında çalışacak fonksiyonu belirle
                client.WriteLineAndGetReply("start", TimeSpan.FromSeconds(1));

                if (client != null)
                {
                    timerChartUpdate.Start();
                }

                // Düğmeyi devre dışı bırak veya diğer UI değişikliklerini yap
                button6.Enabled = false;
                button7.Enabled = true;
            }
            catch (Exception ex)
            {
                // Başlatma hatası durumunda bir hata mesajı göster
                LogError($"Bağlantı başlatılırken hata oluştu: {ex.Message}");
            }
        }


        // button7_Click metodu: butona basıp serial portu kapat
        private void button7_Click(object sender, EventArgs e)
        {
            client.WriteLineAndGetReply("stop", TimeSpan.FromSeconds(1));
            // SimpleTCP sunucuyu durdur
            if (client != null)
            {
                client.Disconnect();
                client.Dispose(); // Nesneyi temizle
            }

            // Düğmeyi etkinleştir veya diğer UI değişikliklerini yapın
            button6.Enabled = true;
            button7.Enabled = false;

            // Timer'ı durdur
            timerChartUpdate.Stop();
        }

        // Client_DataReceived metodu
        private void Client_DataReceived(object sender, Message e)
        {
            // e.MessageString, alınan veriyi içerir
            string data = e.MessageString;

            // Veriyi işleyin ve grafikleri güncelleyin
            try
            {
                ProcessDataBuffer(data);
            }
            catch (Exception ex)
            {
                LogError($"Veri işlenirken hata oluştu: {ex.Message}");
            }
        }

        // TimerChartUpdate_Tick metodu: grafiği tetikleyen timer ayarlarını yapılandır
        private void TimerChartUpdate_Tick(object sender, EventArgs e)
        {
            // Her saniyede bir çalışacak olan metot
            elapsedTimeInSeconds++;

            // Zamanı dijital göstergeye gönder
            digitalGauge1.Value = FormatElapsedTime(elapsedTimeInSeconds / 2);
        }

        private string FormatElapsedTime(int seconds) //bağlantı ekranındaki gösterge
        {
            // Saniyeleri saat, dakika, saniye formatına çevir
            TimeSpan timeSpan = TimeSpan.FromSeconds(seconds);
            return $"{timeSpan.Hours:D2}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
        }

        //serialden gelen veriyi parçalara ayır ve kullan
        private void ProcessDataBuffer(string data)
        {
            try
            {
                Console.WriteLine("Gelen JSON Verisi: " + data); // Konsola JSON verisini yazdır

                // JSON verisini SensorData sınıfına dönüştür
                sensorData = JsonConvert.DeserializeObject<SensorData>(data);

                // Grafikleri güncelle
                UpdateTemperatureChart(sensorData.temperature, sensorData.temperature2, sensorData.temperature3, sensorData.temperature4, sensorData.temperature5);
                UpdateAccelerationChart(sensorData.xAcceleration, sensorData.yAcceleration, sensorData.zAcceleration);
                UpdateGyroChart(sensorData.xGyro, sensorData.yGyro, sensorData.zGyro);
                UpdateSapmaChart(sensorData.sapma_sol, sensorData.sapma_sag, sensorData.sapma_asagi, sensorData.sapma_yukari);
                UpdateVoltageChart(sensorData.voltage);
                UpdateBatteryTemperatureChart(sensorData.temperature_battery);
                UpdateChargeChart(sensorData.charge);
                UpdatePowerChart(sensorData.power);
                UpdateMesafeGauge(sensorData.mesafe);
                UpdateBasincGauge(sensorData.basinc);
                UpdateBatteryInfo();
            }
            catch (JsonException ex)
            {
                // JSON dönüştürme hatası
                LogError("Geçersiz JSON formatı: " + ex.Message);
            }
            catch (Exception ex)
            {
                // Diğer hata durumları
                LogError("Veri işlenirken bir hata oluştu: " + ex.Message);
            }
        }

        public class SensorData
        {
            //grafikler
            public float temperature { get; set; }
            public float temperature2 { get; set; }
            public float temperature3 { get; set; }
            public float temperature4 { get; set; }
            public float temperature5 { get; set; }
            public float xAcceleration { get; set; }
            public float yAcceleration { get; set; }
            public float zAcceleration { get; set; }
            public float xGyro { get; set; }
            public float yGyro { get; set; }
            public float zGyro { get; set; }
            public float sapma_sol { get; set; }
            public float sapma_sag { get; set; }
            public float sapma_asagi { get; set; }
            public float sapma_yukari { get; set; }
            public float voltage { get; set; }
            public float temperature_battery { get; set; }
            public float charge { get; set; }
            public float power { get; set; }

            //göstergeler
            public float mesafe { get; set; }
            public float basinc { get; set; }
        }

        private void LogError(string errorMessage)
        {
            // loglar kısmı

            string dateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string status = "Hata: " + errorMessage;

            // DataGridView'e güvenli bir şekilde erişim sağla
            if (dataGridViewLogs.InvokeRequired)
            {
                dataGridViewLogs.Invoke(new Action(() =>
                {
                    // DataGridView'e yeni satır ekle
                    dataGridViewLogs.Rows.Insert(0, dateTime, status);

                    // En üstteki satıra kaydır
                    dataGridViewLogs.FirstDisplayedScrollingRowIndex = 0;
                }));
            }
            else
            {
                // DataGridView'e yeni satır ekle
                dataGridViewLogs.Rows.Insert(0, dateTime, status);

                // En üstteki satıra kaydır
                dataGridViewLogs.FirstDisplayedScrollingRowIndex = 0;
            }

            // Excel'e veriyi eklemek için listeye ekle
            pendingLogData.Add(new string[] { dateTime, status });

            // Excel'e veriyi işle
            ProcessPendingLogData();
        }

        // Diğer olaylar ve metodlar buraya eklenebilir. Bu kısım daha sonra yapılandırılacaktır.

        private void UpdateBatteryInfo()
        {
            if (batteryProgressBar.InvokeRequired)
            {
                // Eğer başka bir thread üzerinden erişiyorsa, Invoke methodunu kullanarak UI thread'e gönder
                batteryProgressBar.Invoke(new MethodInvoker(() => UpdateBatteryInfo()));
            }
            else
            {
                // Timer her çalıştığında geçen süreyi hesapla
                float elapsedSeconds = elapsedTimeInSeconds/2;
                float dischargeTime = elapsedSeconds - startTime;
                startTime = elapsedSeconds;

                float endCharge = GetCurrentCharge();
                float dischargeRate = CalculateDischargeRate(startCharge, endCharge, dischargeTime);
                startCharge = endCharge;

                // Diğer işlemler
                string powerLevelMessage;

                if (dischargeRate >= 1)
                {
                    powerLevelMessage = "Yüksek düzeyde güç tüketimi!!!";
                }
                else if (dischargeRate >= 0.5)
                {
                    powerLevelMessage = "Orta düzeyde güç tüketimi";
                }
                else
                {
                    powerLevelMessage = "Düşük güç tüketimi...";
                }

                string powerLevelMessage2;

                if (endCharge < 20)
                {
                    powerLevelMessage2 = "Düşük güç uyarısı! Şarj seviyesi çok düşük.";
                    if (!lowBatteryWarningShown)
                    {
                        LogError(powerLevelMessage2);
                        lowBatteryWarningShown = true;
                    }
                }
                else if (endCharge >= 20 && endCharge < 100)
                {
                    powerLevelMessage2 = "Yeterli şarj düzeyi. Şarj seviyesi uygun.";
                }
                else
                {
                    powerLevelMessage2 = "Şarj seviyesi %100. Lütfen prizi çıkartın.";
                }

                labelCycleCount.Text = powerLevelMessage2;

                // Batarya seviyesini ProgressBar'a yansıt
                batteryProgressBar.Value = (int)endCharge;
                batteryLabel.Text = $"{endCharge}%";
                label9.Text = powerLevelMessage;

                // Bataryanın tam kapasitesi
                double fullCapacity = (double)((3.7 * 2000) / 1000);
                labelFullCapacity.Text = $"Tam Kapasite: {fullCapacity} Wh";
            }
        }

        private static float CalculateDischargeRate(float startCharge, float endCharge, float dischargeTime)
        {
            // Şarj azalma hızını hesapla
            float dischargeRate = (startCharge - endCharge) / dischargeTime;
            return dischargeRate;
        }

        private float GetCurrentCharge()
        {
            return sensorData?.charge ?? 0;
        }

        //sıcaklık değerini güncelle
        private void UpdateTemperatureChart(float temperature, float temperature2, float temperature3, float temperature4, float temperature5)
        {
            if (chart1.InvokeRequired)
            {
                // Eğer farklı bir thread'den çağrılıyorsa, UI thread'e Invoke et
                chart1.Invoke(new Action(() => UpdateTemperatureChart(temperature, temperature2, temperature3, temperature4, temperature5)));
            }
            else
            {
                try
                {
                    //grafiğe y değeri saniye, x değeri sıcaklık olan bir seri ekle
                    chart1.Series["Point 1"].Points.AddXY(elapsedTimeInSeconds / 2, temperature);
                    chart1.Series["Point 2"].Points.AddXY(elapsedTimeInSeconds / 2, temperature2);
                    chart1.Series["Point 3"].Points.AddXY(elapsedTimeInSeconds / 2, temperature3);
                    chart1.Series["Point 4"].Points.AddXY(elapsedTimeInSeconds / 2, temperature4);
                    chart1.Series["Point 5"].Points.AddXY(elapsedTimeInSeconds / 2, temperature5);

                    float temperaturee = ((temperature + temperature2 + temperature3 + temperature4 + temperature5)/5);

                    // Label'lara anlık değerleri yazdır
                    label11.Text = $"{temperaturee:F2} °C";

                    //eğer grafiğe eklenen point sayısı 50'yi geçerse eski değerleri sil
                    if (chart1.Series["Point 1"].Points.Count > 50)
                    {
                        chart1.Series["Point 1"].Points.RemoveAt(0);
                        chart1.Series["Point 2"].Points.RemoveAt(0);
                        chart1.Series["Point 3"].Points.RemoveAt(0);
                        chart1.Series["Point 4"].Points.RemoveAt(0);
                        chart1.Series["Point 5"].Points.RemoveAt(0);

                        chart1.ChartAreas[0].AxisX.ScaleView.Position = chart1.Series["Point 1"].Points[0].XValue;
                        chart1.ChartAreas[0].AxisX.ScaleView.Size = 50; // 50 veriyi göster

                        chart1.Update();
                    }
                }
                catch (Exception)
                {
                    // Veri alınamama hatası
                    LogError("Sıcaklık sensöründen veri alınamıyor...");
                }
            }
        }

        //ivme grafiğini güncelle
        private void UpdateAccelerationChart(float xAcceleration, float yAcceleration, float zAcceleration)
        {
            if (chart4.InvokeRequired)
            {
                // Eğer farklı bir thread'den çağrılıyorsa, UI thread'e Invoke et
                chart4.Invoke(new Action(() => UpdateAccelerationChart(xAcceleration, yAcceleration, zAcceleration)));
            }
            else
            {
                try
                {
                    // UI thread üzerinden işlemleri gerçekleştir
                    chart4.Series["xValues"].Points.AddXY(elapsedTimeInSeconds / 2, xAcceleration);
                    chart4.Series["yValues"].Points.AddXY(elapsedTimeInSeconds / 2, yAcceleration);
                    chart4.Series["zValues"].Points.AddXY(elapsedTimeInSeconds / 2, zAcceleration);

                    // X Yönünde Hız hesapla ((şu andaki ivme - önceki ivme) * zaman)
                    float speed = Math.Abs((elapsedTimeInSeconds / 2) * (xAcceleration - previousXAcceleration));
                    previousXAcceleration = xAcceleration; // Önceki ivmeyi güncelle

                    // Y Yönünde Hız hesapla ((şu andaki ivme - önceki ivme) * zaman)
                    float speed2 = Math.Abs((elapsedTimeInSeconds / 2) * (yAcceleration - previousYAcceleration));
                    previousYAcceleration = yAcceleration; // Önceki ivmeyi güncelle

                    label21.Text = $"{speed2:F2}";

                    // Z Yönünde Hız hesapla ((şu andaki ivme - önceki ivme) * zaman)
                    float speed3 = Math.Abs((elapsedTimeInSeconds / 2) * (zAcceleration - previousZAcceleration));
                    previousZAcceleration = zAcceleration; // Önceki ivmeyi güncelle

                    label22.Text = $"{speed3:F2}";

                    // Gauge'ı güncelle
                    UpdateSpeedGauge(speed);

                    // Label'lara anlık değerleri yazdır
                    labelXAcceleration.Text = $"X: {xAcceleration:F2}";
                    labelYAcceleration.Text = $"Y: {yAcceleration:F2}";
                    labelZAcceleration.Text = $"Z: {zAcceleration:F2}";

                    //eğer grafiğe eklenen point sayısı 50'yi geçerse eski değerleri sil
                    if (chart4.Series["xValues"].Points.Count > 30)
                    {
                        chart4.Series["xValues"].Points.RemoveAt(0);
                        chart4.Series["yValues"].Points.RemoveAt(0);
                        chart4.Series["zValues"].Points.RemoveAt(0);

                        chart4.ChartAreas[0].AxisX.ScaleView.Position = chart4.Series["xValues"].Points[0].XValue;
                        chart4.ChartAreas[0].AxisX.ScaleView.Size = 20; // 20 veriyi göster

                        chart4.Update();
                    }
                }
                catch (Exception)
                {
                    // Veri alınamama hatası
                    LogError("İvme sensöründen veri alınamıyor...");
                }
            }
        }

        private void UpdateGyroChart(float xGyro, float yGyro, float zGyro)
        {
            if (chart5.InvokeRequired)
            {
                // Eğer farklı bir thread'den çağrılıyorsa, UI thread'e Invoke et
                chart5.Invoke(new Action(() => UpdateGyroChart(xGyro, yGyro, zGyro)));
            }
            else
            {
                try
                {
                    // UI thread üzerinden işlemleri gerçekleştir
                    chart5.Series["Roll"].Points.AddXY(elapsedTimeInSeconds / 2, xGyro);
                    chart5.Series["Pitch"].Points.AddXY(elapsedTimeInSeconds / 2, yGyro);
                    chart5.Series["Yaw"].Points.AddXY(elapsedTimeInSeconds / 2, zGyro);

                    // Label'lara anlık değerleri yazdır
                    gyro1.Text = $"Roll: {xGyro:F2}";
                    gyro2.Text = $"Pitch: {yGyro:F2}";
                    gyro3.Text = $"Yaw: {zGyro:F2}";

                    //eğer grafiğe eklenen point sayısı 50'yi geçerse eski değerleri sil
                    if (chart5.Series["Roll"].Points.Count > 30)
                    {
                        chart5.Series["Roll"].Points.RemoveAt(0);
                        chart5.Series["Pitch"].Points.RemoveAt(0);
                        chart5.Series["Yaw"].Points.RemoveAt(0);

                        chart5.ChartAreas[0].AxisX.ScaleView.Position = chart5.Series["Roll"].Points[0].XValue;
                        chart5.ChartAreas[0].AxisX.ScaleView.Size = 20; // 20 veriyi göster

                        chart5.Update();
                    }
                }
                catch (Exception)
                {
                    // Veri alınamama hatası
                    LogError("Gyro sensörden veri alınamıyor...");
                }
            }
        }

        private void UpdateSapmaChart(float sapma_sol, float sapma_sag, float sapma_asagi, float sapma_yukari)
        {
            if (chart6.InvokeRequired)
            {
                // Eğer farklı bir thread'den çağrılıyorsa, UI thread'e Invoke et
                chart6.Invoke(new Action(() => UpdateSapmaChart(sapma_sol, sapma_sag, sapma_asagi, sapma_yukari)));
            }
            else
            {
                try
                {
                    // UI thread üzerinden işlemleri gerçekleştir
                    chart6.Series["Sol"].Points.AddXY(elapsedTimeInSeconds / 2, sapma_sol);
                    chart6.Series["Sağ"].Points.AddXY(elapsedTimeInSeconds / 2, sapma_sag);
                    chart6.Series["Aşağı"].Points.AddXY(elapsedTimeInSeconds / 2, sapma_asagi);
                    chart6.Series["Yukarı"].Points.AddXY(elapsedTimeInSeconds / 2, sapma_yukari);

                    //eğer grafiğe eklenen point sayısı 50'yi geçerse eski değerleri sil
                    if (chart6.Series["Sol"].Points.Count > 30)
                    {
                        chart6.Series["Sol"].Points.RemoveAt(0);
                        chart6.Series["Sağ"].Points.RemoveAt(0);
                        chart6.Series["Aşağı"].Points.RemoveAt(0);
                        chart6.Series["Yukarı"].Points.RemoveAt(0);

                        chart6.ChartAreas[0].AxisX.ScaleView.Position = chart6.Series["Sol"].Points[0].XValue;
                        chart6.ChartAreas[0].AxisX.ScaleView.Size = 20; // 20 veriyi göster

                        chart6.Update();
                    }
                }
                catch (Exception)
                {
                    // Veri alınamama hatası
                    LogError("Sapma verileri alınamıyor...");
                }
            }
        }

        private void UpdateVoltageChart(float voltage)
        {
            if (chart2.InvokeRequired)
            {
                // Eğer farklı bir thread'den çağrılıyorsa, UI thread'e Invoke et
                chart2.Invoke(new Action(() => UpdateVoltageChart(voltage)));
            }
            else
            {
                try
                {
                    //grafiğe y değeri saniye, x değeri sıcaklık olan bir seri ekle
                    chart2.Series["voltaj"].Points.AddXY(elapsedTimeInSeconds / 2, voltage);

                    //eğer grafiğe eklenen point sayısı 50'yi geçerse eski değerleri sil
                    if (chart2.Series["voltaj"].Points.Count > 50)
                    {
                        chart2.Series["voltaj"].Points.RemoveAt(0);

                        chart2.ChartAreas[0].AxisX.ScaleView.Position = chart2.Series["voltaj"].Points[0].XValue;
                        chart2.ChartAreas[0].AxisX.ScaleView.Size = 50; // 50 veriyi göster

                        chart2.Update();
                    }
                }
                catch (Exception)
                {
                    // Veri alınamama hatası
                    LogError("Voltaj verisi alınamıyor...");
                }
            }
        }

        private void UpdateBatteryTemperatureChart(float temperature_battery)
        {
            if (chart3.InvokeRequired)
            {
                // Eğer farklı bir thread'den çağrılıyorsa, UI thread'e Invoke et
                chart3.Invoke(new Action(() => UpdateBatteryTemperatureChart(temperature_battery)));
            }
            else
            {
                try
                {
                    //grafiğe y değeri saniye, x değeri sıcaklık olan bir seri ekle
                    chart3.Series["Sıcaklık"].Points.AddXY(elapsedTimeInSeconds / 2, temperature_battery);

                    //eğer grafiğe eklenen point sayısı 50'yi geçerse eski değerleri sil
                    if (chart3.Series["Sıcaklık"].Points.Count > 50)
                    {
                        chart3.Series["Sıcaklık"].Points.RemoveAt(0);

                        chart3.ChartAreas[0].AxisX.ScaleView.Position = chart3.Series["Sıcaklık"].Points[0].XValue;
                        chart3.ChartAreas[0].AxisX.ScaleView.Size = 50; // 50 veriyi göster

                        chart3.Update();
                    }
                }
                catch (Exception)
                {
                    // Veri alınamama hatası
                    LogError("Batarya sıcaklık verisi alınamıyor...");
                }
            }
        }

        private void UpdateChargeChart(float charge)
        {
            if (chart7.InvokeRequired)
            {
                // Eğer farklı bir thread'den çağrılıyorsa, UI thread'e Invoke et
                chart7.Invoke(new Action(() => UpdateChargeChart(charge)));
            }
            else
            {
                try
                {
                    //grafiğe y değeri saniye, x değeri sıcaklık olan bir seri ekle
                    chart7.Series["şarj"].Points.AddXY(elapsedTimeInSeconds / 2, charge);

                    //eğer grafiğe eklenen point sayısı 50'yi geçerse eski değerleri sil
                    if (chart7.Series["şarj"].Points.Count > 50)
                    {
                        chart7.Series["şarj"].Points.RemoveAt(0);

                        chart7.ChartAreas[0].AxisX.ScaleView.Position = chart7.Series["şarj"].Points[0].XValue;
                        chart7.ChartAreas[0].AxisX.ScaleView.Size = 50; // 50 veriyi göster

                        chart7.Update();
                    }
                }
                catch (Exception)
                {
                    // Veri alınamama hatası
                    LogError("Şarj düzeyi verisi alınamıyor...");
                }
            }
        }

        private void UpdatePowerChart(float power)
        {
            if (chart8.InvokeRequired)
            {
                // Eğer farklı bir thread'den çağrılıyorsa, UI thread'e Invoke et
                chart8.Invoke(new Action(() => UpdatePowerChart(power)));
            }
            else
            {
                try
                {
                    //grafiğe y değeri saniye, x değeri sıcaklık olan bir seri ekle
                    chart8.Series["güç"].Points.AddXY(elapsedTimeInSeconds / 2, power);

                    //eğer grafiğe eklenen point sayısı 50'yi geçerse eski değerleri sil
                    if (chart8.Series["güç"].Points.Count > 50)
                    {
                        chart8.Series["güç"].Points.RemoveAt(0);

                        chart8.ChartAreas[0].AxisX.ScaleView.Position = chart8.Series["güç"].Points[0].XValue;
                        chart8.ChartAreas[0].AxisX.ScaleView.Size = 50; // 50 veriyi göster

                        chart8.Update();
                    }
                }
                catch (Exception)
                {
                    // Veri alınamama hatası
                    LogError("Güç verisi alınamıyor...");
                }
            }
        }

        private void UpdateSpeedGauge(float speed)
        {
            if (radialGauge1.InvokeRequired)
            {
                // Eğer farklı bir thread'den çağrılıyorsa, UI thread'e Invoke et
                radialGauge1.Invoke(new Action(() => UpdateSpeedGauge(speed)));
            }
            else
            {
                // hız 0-50 aralığındaysa yeşil, 50-100 aralığındaysa sarı, 100-150 aralığındaysa kırmızı renk kullanılmıştır.
                Color renk;
                if (speed <= 66)
                {
                    renk = Color.Green;
                }
                else if (speed <= 132)
                {
                    renk = Color.Yellow;
                }
                else
                {
                    renk = Color.Red;
                }

                // Range'leri temizle
                radialGauge1.Ranges.Clear();

                // Yeni range ekle
                radialGauge1.Ranges.Add(new Syncfusion.Windows.Forms.Gauge.Range
                {
                    StartValue = 0,
                    EndValue = speed,
                    Color = renk,
                });

                // Radial Gauge'ı güncelle
                radialGauge1.Value = speed;

                // Radial Gauge'ı güncel tut
                radialGauge1.Invalidate();
            }
        }

        private void UpdateMesafeGauge(float mesafe)
        {
            if (radialGauge2.InvokeRequired)
            {
                // Eğer farklı bir thread'den çağrılıyorsa, UI thread'e Invoke et
                radialGauge2.Invoke(new Action(() => UpdateMesafeGauge(mesafe)));
            }
            else
            {
                try

                {
                    // hız 0-50 aralığındaysa yeşil, 50-100 aralığındaysa sarı, 100-150 aralığındaysa kırmızı renk kullanılmıştır.
                    Color renk;
                    if (mesafe <= 70)
                    {
                        renk = Color.Green;
                    }
                    else if (mesafe <= 140)
                    {
                        renk = Color.Yellow;
                    }
                    else
                    {
                        renk = Color.Red;
                    }

                    // Range'leri temizle
                    radialGauge2.Ranges.Clear();

                    // Yeni range ekle
                    radialGauge2.Ranges.Add(new Syncfusion.Windows.Forms.Gauge.Range
                    {
                        StartValue = 0,
                        EndValue = mesafe,
                        Color = renk,
                    });

                    // Radial Gauge'ı güncelle
                    radialGauge2.Value = mesafe;

                    // Radial Gauge'ı güncel tut
                    radialGauge2.Invalidate();
                }
                catch (Exception)
                {
                    // Veri alınamama hatası
                    LogError("Mesafe verisi alınamıyor...");
                }
            }
        }

        private void UpdateBasincGauge(float basinc)
        {
            if (radialGauge3.InvokeRequired)
            {
                // Eğer farklı bir thread'den çağrılıyorsa, UI thread'e Invoke et
                radialGauge3.Invoke(new Action(() => UpdateBasincGauge(basinc)));
            }
            else
            {
                try
                {
                    // hız 0-50 aralığındaysa yeşil, 50-100 aralığındaysa sarı, 100-150 aralığındaysa kırmızı renk kullanılmıştır.
                    Color renk;
                    if (basinc <= 100)
                    {
                        renk = Color.Green;
                    }
                    else if (basinc <= 200)
                    {
                        renk = Color.Yellow;
                    }
                    else
                    {
                        renk = Color.Red;
                    }

                    // Range'leri temizle
                    radialGauge3.Ranges.Clear();

                    // Yeni range ekle
                    radialGauge3.Ranges.Add(new Syncfusion.Windows.Forms.Gauge.Range
                    {
                        StartValue = 0,
                        EndValue = basinc,
                        Color = renk,
                    });

                    // Radial Gauge'ı güncelle
                    radialGauge3.Value = basinc;

                    // Radial Gauge'ı güncel tut
                    radialGauge3.Invalidate();
                }
                catch (Exception)
                {
                    // Veri alınamama hatası
                    LogError("Basınç verisi alınamıyor...");
                }
            }
        }

        private void button9_Click(object sender, EventArgs e) //eğer logları durdurmak istersen bu butona bas
        {
            if (timerChartUpdate.Enabled)
            {
                timerChartUpdate.Stop();
            }
        }
        private void button10_Click(object sender, EventArgs e) //eğer durdurulmuş olan logları yeniden başlatmak istersen bu butona bas
        {
            if (!timerChartUpdate.Enabled)
            {
                timerChartUpdate.Start();
            }
        }

        private void SaveAsPdfButton_Click(object sender, EventArgs e)
        {
            try
            {
                SaveFileDialog saveFileDialog1 = new SaveFileDialog();
                saveFileDialog1.Filter = "PDF Dosyası (*.pdf)|*.pdf";
                saveFileDialog1.Title = "PDF olarak Kaydet";

                if (saveFileDialog1.ShowDialog() == DialogResult.OK)
                {
                    string pdfFilePath = saveFileDialog1.FileName;

                    using (PdfDocument document = new PdfDocument())
                    {
                        // Birinci Sayfa
                        PdfPage page1 = document.Pages.Add();
                        PdfGraphics graphics1 = page1.Graphics;

                        AddChartToPdf(graphics1, chart1, "Sicaklik Grafigi", 10, 10, 301, 365);
                        AddChartToPdf(graphics1, chart4, "Ivme Grafigi", 10, 410, 200, 137);
                        AddChartToPdf(graphics1, chart5, "Gyro Grafigi", 10, 580, 200, 137);

                        // İkinci Sayfa
                        PdfPage page2 = document.Pages.Add();
                        PdfGraphics graphics2 = page2.Graphics;

                        AddChartToPdf(graphics2, chart6, "Sapma (Asagi-Yukari / Sag-Sol)", 10, 5, 200, 137);
                        AddChartToPdf(graphics2, chart2, "Sarj Giris Voltaji", 10, 170, 490, 270);
                        AddChartToPdf(graphics2, chart3, "Sicaklik Analizi", 10, 470, 490, 270);

                        // Üçüncü Sayfa
                        PdfPage page3 = document.Pages.Add();
                        PdfGraphics graphics3 = page3.Graphics;

                        AddChartToPdf(graphics3, chart7, "Sarj Duzeyi", 10, 10, 490, 270);
                        AddChartToPdf(graphics3, chart8, "Guc", 10, 310, 490, 270);
                        AddDigitalGaugeToPdf(graphics3, digitalGauge1, "Kronometre-Gecen Sure", 10, 620);

                        // Dördüncü Sayfa
                        PdfPage page4 = document.Pages.Add();
                        PdfGraphics graphics4 = page4.Graphics;

                        AddRadialGaugeToPdf(graphics4, radialGauge1, "Hiz", 10, 10);
                        AddRadialGaugeToPdf(graphics4, radialGauge2, "Mesafe", 200, 10);
                        AddRadialGaugeToPdf(graphics4, radialGauge3, "Basinc", 10, 190);
                        AddTrackBarToPdf(graphics4, trackBar1, "Itki", 10, 570);
                        AddTrackBarToPdf(graphics4, trackBar2, "Levitasyon", 10, 630);

                        document.Save(pdfFilePath);
                        Process.Start(pdfFilePath);
                    }

                    MessageBox.Show("PDF olarak başarıyla kaydedildi.", "Başarı", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Hata: {ex.Message}", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void AddChartToPdf(PdfGraphics graphics, System.Windows.Forms.DataVisualization.Charting.Chart chart, string chartTitle, float x, float y, float width, float height)
        {
            MemoryStream chartImageStream = new MemoryStream();
            chart.SaveImage(chartImageStream, ChartImageFormat.Png);
            PdfBitmap pdfBitmap = new PdfBitmap(chartImageStream);

            graphics.DrawString(chartTitle, new PdfStandardFont(PdfFontFamily.Helvetica, 14), PdfBrushes.Black, new PointF(x, y));

            // Belirtilen boyutlarda grafik resmini çiz
            graphics.DrawImage(pdfBitmap, new RectangleF(x, y + 14 + 10, width, height));

            chartImageStream.Dispose();
        }

        private void AddDigitalGaugeToPdf(PdfGraphics graphics, DigitalGauge digitalGauge, string gaugeTitle, float x, float y)
        {
            // RadialGauge'u içeren bir Bitmap oluştur
            Bitmap gaugeImage = new Bitmap(digitalGauge.Width, digitalGauge.Height);

            // RadialGauge'u Bitmap'e çiz
            digitalGauge.DrawToBitmap(gaugeImage, new System.Drawing.Rectangle(0, 0, gaugeImage.Width, gaugeImage.Height));

            // Bitmap'i MemoryStream'e kaydet
            MemoryStream gaugeImageStream = new MemoryStream();
            gaugeImage.Save(gaugeImageStream, ImageFormat.Png);

            // MemoryStream'den PdfBitmap oluştur
            PdfBitmap pdfBitmap = new PdfBitmap(gaugeImageStream);

            // Başlığı ekle
            graphics.DrawString(gaugeTitle, new PdfStandardFont(PdfFontFamily.Helvetica, 14), PdfBrushes.Black, new PointF(x, y));

            // Görüntüyü ekle
            graphics.DrawImage(pdfBitmap, new PointF(x, y + 14 + 10));

            // Bellek akışını temizle
            gaugeImageStream.Dispose();
        }

        private void AddRadialGaugeToPdf(PdfGraphics graphics, RadialGauge radialGauge, string gaugeTitle, float x, float y)
        {
            // RadialGauge'u içeren bir Bitmap oluştur
            Bitmap gaugeImage = new Bitmap(radialGauge.Width, radialGauge.Height);

            // RadialGauge'u Bitmap'e çiz
            radialGauge.DrawToBitmap(gaugeImage, new System.Drawing.Rectangle(0, 0, gaugeImage.Width, gaugeImage.Height));

            // Bitmap'i MemoryStream'e kaydet
            MemoryStream gaugeImageStream = new MemoryStream();
            gaugeImage.Save(gaugeImageStream, ImageFormat.Png);

            // MemoryStream'den PdfBitmap oluştur
            PdfBitmap pdfBitmap = new PdfBitmap(gaugeImageStream);

            // Başlığı ekle
            graphics.DrawString(gaugeTitle, new PdfStandardFont(PdfFontFamily.Helvetica, 14), PdfBrushes.Black, new PointF(x, y));

            // Görüntüyü ekle
            graphics.DrawImage(pdfBitmap, new PointF(x, y + 14 + 10));

            // Bellek akışını temizle
            gaugeImageStream.Dispose();
        }

        private void AddTrackBarToPdf(PdfGraphics graphics, TrackBar trackBar, string trackBarTitle, float x, float y)
        {
            // TrackBar'ı içeren bir Bitmap oluştur
            Bitmap trackBarImage = new Bitmap(trackBar.Width, trackBar.Height);

            // TrackBar'ı Bitmap'e çiz
            trackBar.DrawToBitmap(trackBarImage, new System.Drawing.Rectangle(0, 0, trackBarImage.Width, trackBarImage.Height));

            // Bitmap'i MemoryStream'e kaydet
            MemoryStream trackBarImageStream = new MemoryStream();
            trackBarImage.Save(trackBarImageStream, ImageFormat.Png);

            // MemoryStream'den PdfBitmap oluştur
            PdfBitmap pdfBitmap = new PdfBitmap(trackBarImageStream);

            // Başlığı ekle
            graphics.DrawString(trackBarTitle, new PdfStandardFont(PdfFontFamily.Helvetica, 14), PdfBrushes.Black, new PointF(x, y));

            // Görüntüyü ekle
            graphics.DrawImage(pdfBitmap, new PointF(x, y + 14 + 10));

            // Bellek akışını temizle
            trackBarImageStream.Dispose();
        }


        private void calculateButton_Click(object sender, EventArgs e)
        {
            try
            {
                // Girişleri al
                double steelAmount = Convert.ToDouble(steelAmountTextBox.Text);
                double steelCarbonPerTon = Convert.ToDouble(steelCarbonPerTonTextBox.Text);
                double electricityUsage = Convert.ToDouble(electricityUsageTextBox.Text);
                double electricityCarbonPerKWh = Convert.ToDouble(electricityCarbonPerKWhTextBox.Text);

                // Hesaplamalar
                double productionEmissions = steelAmount * steelCarbonPerTon;
                double usageEmissions = electricityUsage * electricityCarbonPerKWh;

                // Toplam karbon ayak izi
                double totalCarbonFootprint = productionEmissions + usageEmissions;

                // Sonucu label'da göster
                resultLabel.Text = $"Toplam: {totalCarbonFootprint} ton CO2";
            }
            catch (FormatException)
            {
                MessageBox.Show("Lütfen geçerli sayısal değerler giriniz.", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void steelAmountTextBox_Click(object sender, EventArgs e)
        {
            steelAmountTextBox.Clear();
        }

        private void steelCarbonPerTonTextBox_Click(object sender, EventArgs e)
        {
            steelCarbonPerTonTextBox.Clear();
        }

        private void electricityUsageTextBox_Click(object sender, EventArgs e)
        {
            electricityUsageTextBox.Clear();
        }

        private void electricityCarbonPerKWhTextBox_Click(object sender, EventArgs e)
        {
            electricityCarbonPerKWhTextBox.Clear();
        }

        private void AddImageToPdf(PdfGraphics graphics, string imagePath, float x, float y, float width, float height)
        {
            // Görseli PDF sayfasına eklemek için PdfImage nesnesini kullan
            PdfImage pdfImage = PdfImage.FromFile(imagePath);

            // Görseli belirtilen konum ve boyutlarda çiz
            graphics.DrawImage(pdfImage, x, y, width, height);
        }

        private void generatePdfButton_Click(object sender, EventArgs e)
        {
            try
            {
                // PDF dosyasını oluştur
                SaveFileDialog saveFileDialog1 = new SaveFileDialog();
                saveFileDialog1.Filter = "PDF Dosyası (*.pdf)|*.pdf";
                saveFileDialog1.Title = "PDF olarak Kaydet";

                if (saveFileDialog1.ShowDialog() == DialogResult.OK)
                {
                    string pdfFilePath = saveFileDialog1.FileName;

                    // PDF belgesini oluştur
                    using (PdfDocument document = new PdfDocument())
                    {
                        // Sayfa ekleyin
                        PdfPage page = document.Pages.Add();

                        // Sayfa grafiklerini alın
                        PdfGraphics graphics = page.Graphics;

                        // Girişleri al
                        double steelAmount = Convert.ToDouble(steelAmountTextBox.Text);
                        double steelCarbonPerTon = Convert.ToDouble(steelCarbonPerTonTextBox.Text);
                        double electricityUsage = Convert.ToDouble(electricityUsageTextBox.Text);
                        double electricityCarbonPerKWh = Convert.ToDouble(electricityCarbonPerKWhTextBox.Text);

                        // Hesaplamalar
                        double productionEmissions = steelAmount * steelCarbonPerTon;
                        double usageEmissions = electricityUsage * electricityCarbonPerKWh;
                        double totalCarbonFootprint = productionEmissions + usageEmissions;

                        // PDF'e sonuçları ekle
                        graphics.DrawString($"Üretim Emisyonlari: {productionEmissions} ton CO2", new PdfStandardFont(PdfFontFamily.TimesRoman, 12), PdfBrushes.Black, new PointF(10, 10));
                        graphics.DrawString($"Elektrik Kullanimi Emisyonlari: {usageEmissions} ton CO2", new PdfStandardFont(PdfFontFamily.TimesRoman, 12), PdfBrushes.Black, new PointF(10, 30));
                        graphics.DrawString($"Toplam Karbon Ayak Izi: {totalCarbonFootprint} ton CO2", new PdfStandardFont(PdfFontFamily.TimesRoman, 12), PdfBrushes.Black, new PointF(10, 50));

                        // Tavsiyeleri ekle
                        graphics.DrawString("Öneriler:", new PdfStandardFont(PdfFontFamily.TimesRoman, 12), PdfBrushes.Black, new PointF(10, 80));
                        graphics.DrawString("- Daha sürdürülebilir üretim yöntemlerini düsünün.", new PdfStandardFont(PdfFontFamily.TimesRoman, 12), PdfBrushes.Black, new PointF(10, 100));
                        graphics.DrawString("- Enerji verimliligini artirmak için elektrik kullaniminizi gözden geçirin.", new PdfStandardFont(PdfFontFamily.TimesRoman, 12), PdfBrushes.Black, new PointF(10, 120));
                        graphics.DrawString("- Ortalama bir benzinli araç üretilirken 5.6 ton CO2 salinimi olmaktadir. Bu oran geçilmemelidir.", new PdfStandardFont(PdfFontFamily.TimesRoman, 12), PdfBrushes.Black, new PointF(10, 140));
                        graphics.DrawString("- Sistemde kullanilan malzemelerin büyük bir kisminin geri dönüstürülebilir olmasi saglanmalidir.", new PdfStandardFont(PdfFontFamily.TimesRoman, 12), PdfBrushes.Black, new PointF(10, 160));

                        // Görseli PDF'e ekle
                        AddImageToPdf(graphics, "C:/surdurulebilirlik-bilesenleri.jpg", 10, 210, 366, 340);

                        // PDF dosyasını kaydedin
                        document.Save(pdfFilePath);
                        Process.Start(pdfFilePath);

                        MessageBox.Show("PDF olarak başarıyla kaydedildi.", "Başarı", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Hata: {ex.Message}", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void button5_Click(object sender, EventArgs e)
        {
            // Yeni formu oluştur
            SSHTerminal SSHTerminal = new SSHTerminal();

            // Yeni formu göster
            SSHTerminal.Show();
        }

        private void pictureBox6_Click(object sender, EventArgs e)
        {
            // Yardım penceresini oluşturun ve gösterin
            helpForm helpForm = new helpForm();
            helpForm.Show();
        }

        private void button8_Click(object sender, EventArgs e)
        {
            client.WriteLineAndGetReply("stop", TimeSpan.FromSeconds(1));
        }

        private void button1_Click(object sender, EventArgs e)
        {
            client.WriteLineAndGetReply("levitasyon", TimeSpan.FromSeconds(1));
        }

        private void button2_Click(object sender, EventArgs e)
        {
            client.WriteLineAndGetReply("hizlan", TimeSpan.FromSeconds(1));
        }

        private void button3_Click(object sender, EventArgs e)
        {
            client.WriteLineAndGetReply("fren", TimeSpan.FromSeconds(1));
        }

        private void button4_Click(object sender, EventArgs e)
        {
            client.WriteLineAndGetReply("dur", TimeSpan.FromSeconds(1));
        }

        private void trackBar1_ValueChanged(object sender, EventArgs e)
        {
            string itkivalue = trackBar1.Value.ToString();
            label15.Text = itkivalue;
            string text = "itki:" + itkivalue;
            client.WriteLineAndGetReply(text, TimeSpan.FromSeconds(1));
        }

        private void trackBar2_ValueChanged(object sender, EventArgs e)
        {
            string levitasyonvalue = trackBar2.Value.ToString();
            label25.Text = levitasyonvalue;
            string text = "levitasyon:" + levitasyonvalue;
            client.WriteLineAndGetReply(text, TimeSpan.FromSeconds(1));
        }
    }
}
