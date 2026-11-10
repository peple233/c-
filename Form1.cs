using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace bluetooth_ctrol
{

    public partial class Form1 : Form
    {
        bool serioPort = false;  //显示串口打开或关闭
        bool byte4_datareceive = false; //4byte数据接收关闭
        bool byte1_datareceive = false; //1byte数据接收关闭

        byte[] hardware1 = { 0xaa, 0x55, 0xf0 };//温度    默认数值校准数据
        byte[] hardware2 = { 0xaa, 0x99, 0xf0 };//湿度    默认数值校准数据
        byte[] hardware3 = { 0xaa, 0xcd, 0xf0 };//光照    默认数值校准数据

        byte[] hardware_self1 = { 0, 0, 0 };//温度    自定义数值校准数据
        byte[] hardware_self2 = { 0, 0, 0 };//湿度    自定义数值校准数据
        byte[] hardware_self3 = { 0, 0, 0 };//光照    自定义数值校准数据

        byte byte_data; //临时存储传感器数据

        byte data_temp, data_humi, data_light; //存储传感器数据

        //1byte变量
        string datahead = "";
        string databody = "";
        string datatail = "";

        // 图表曲线对象
        private ScottPlot.Plottables.SignalXY sigTemp;
        private ScottPlot.Plottables.SignalXY sigHumi;
        private ScottPlot.Plottables.SignalXY sigLight;

        // 再新增时间列表 ↓
        private readonly List<DateTime> tempTime = new List<DateTime>();
        private readonly List<DateTime> humiTime = new List<DateTime>();
        private readonly List<DateTime> lightTime = new List<DateTime>();

        // 使用 List<double> 作为数据源
        private readonly List<double> tempData = new List<double>();
        private readonly List<double> humiData = new List<double>();
        private readonly List<double> lightData = new List<double>();

        public Form1()
        {
            InitializeComponent();
            this.FormClosing += Form1_FormClosing;
        }

        private void OpenDataFolder()
        {
            string root = Path.Combine(Application.StartupPath, "Data");
            if (!Directory.Exists(root))
            {
                Directory.CreateDirectory(root); // 没有就创建
            }

            // 用系统资源管理器打开
            System.Diagnostics.Process.Start("explorer.exe", root);
        }

        private void SaveToCsv(List<DateTime> time, List<double> value, string sensorName)
        {
            if (time.Count == 0 || value.Count == 0)
            {
                MessageBox.Show($"{sensorName} 暂无数据，无法保存。", "提示");
                return;
            }

            string folder = Path.Combine(Application.StartupPath, "Data", sensorName);
            Directory.CreateDirectory(folder);

            string fileName = Path.Combine(folder,
                $"{sensorName}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

            // C# 7.3 写法：传统 using
            using (var sw = new StreamWriter(fileName, false, Encoding.UTF8))
            {
                sw.WriteLine("时间,值");
                for (int i = 0; i < time.Count && i < value.Count; i++)
                    sw.WriteLine($"{time[i]:yyyy-MM-dd HH:mm:ss},{value[i]}");
            }

            MessageBox.Show($"已保存：\n{fileName}", "保存成功");
            File.WriteAllText(Path.Combine(folder, "README.txt"),
            "请使用 Excel 打开 CSV 文件查看数据。\r\n时间列格式：yyyy-MM-dd HH:mm:ss\r\n");
        }

        private void Form1_Load(object sender, EventArgs e)//启动时加载项
        {
            //启用鼠标滚轮缩放 + 左键拖拽平移
            formsPlot1.UserInputProcessor.Reset();
            formsPlot2.UserInputProcessor.Reset();
            formsPlot3.UserInputProcessor.Reset();

            // === 图表初始化（时间轴版） ===
            //formsPlot1.Plot.XLabel("时间");
            formsPlot1.Plot.YLabel("温度",15);
/*            formsPlot2.Plot.XLabel("时间")*/;
            formsPlot2.Plot.YLabel("湿度",15);
            //formsPlot3.Plot.XLabel("时间");
            formsPlot3.Plot.YLabel("光照", 15);
            formsPlot1.Plot.Font.Automatic();
            formsPlot2.Plot.Font.Automatic();
            formsPlot3.Plot.Font.Automatic();

            // 放 2 个占位点，避免空图
            var now = DateTime.Now;
            tempTime.Add(now); tempData.Add(0);
            humiTime.Add(now); humiData.Add(0);
            lightTime.Add(now); lightData.Add(0);

            // 创建 SignalXY
            sigTemp = formsPlot1.Plot.Add.SignalXY(tempTime.Select(t => t.ToOADate()).ToArray(), tempData.ToArray());
            sigHumi = formsPlot2.Plot.Add.SignalXY(humiTime.Select(t => t.ToOADate()).ToArray(), humiData.ToArray());
            sigLight = formsPlot3.Plot.Add.SignalXY(lightTime.Select(t => t.ToOADate()).ToArray(), lightData.ToArray());


            sigTemp.Color = ScottPlot.Colors.Red;
            sigHumi.Color = ScottPlot.Colors.Blue;
            sigLight.Color = ScottPlot.Colors.Green;

            foreach (var sig in new[] { sigTemp, sigHumi, sigLight })
            {
                sig.LineWidth = 2;
                sig.MarkerSize = 0;
            }
       
            // 时间轴刻度 + 最近 30 秒窗口
            formsPlot1.Plot.Axes.DateTimeTicksBottom();
            formsPlot2.Plot.Axes.DateTimeTicksBottom();
            formsPlot3.Plot.Axes.DateTimeTicksBottom();

            double right = now.ToOADate();
            double left = now.AddSeconds(-30).ToOADate();
            formsPlot1.Plot.Axes.SetLimitsX(left, right);
            formsPlot2.Plot.Axes.SetLimitsX(left, right);
            formsPlot3.Plot.Axes.SetLimitsX(left, right);

            formsPlot1.Refresh();
            formsPlot2.Refresh();
            formsPlot3.Refresh();
            ///////////////////////////////////////---------

            //初始化接收，发射方式
            checkBox1.Checked = true;
            checkBox3.Checked = true;


            ////设置接收。发送文本框的颜色
            // 1. 先把已有文本整体变红
            richTextBoxRx.SelectAll();                 // 选中全部
            richTextBoxRx.SelectionColor = Color.Red;  // 统一红色
            richTextBoxRx.Select(0, 0);                // 取消选中
            // 2. 保证以后追加的文字也是红色
            richTextBoxRx.ForeColor = Color.Red;       // 默认前景色设为红色

            ////设置接收。发送文本框的颜色
            // 1. 先把已有文本整体变蓝色
            richTextBoxTx.SelectAll();                 // 选中全部
            richTextBoxTx.SelectionColor = Color.Blue;  // 统一红色
            richTextBoxTx.Select(0, 0);                // 取消选中
            // 2. 保证以后追加的文字也是蓝色
            richTextBoxTx.ForeColor = Color.Blue;       // 默认前景色设为蓝色


            checkBox8.Checked = true;


            //串口获取
                bitspeed_select.Text= "9600";  
                // 1. 获取所有可用串口
                string[] ports = SerialPort.GetPortNames();   // 返回形如 {"COM1","COM3"}

                // 2. 绑定到 ComboBox
                COM_SELECT.Items.Clear();
                COM_SELECT.Items.AddRange(ports);

                // 3. 默认选第一个（如果有）
                if (ports.Length > 0)
                COM_SELECT.SelectedIndex = 0;



            btnSaveTemp.Click += (s, x) => SaveToCsv(tempTime, tempData, "Temp");
            btnSaveHumi.Click += (s, x) => SaveToCsv(humiTime, humiData, "Humi");
            btnSaveLight.Click += (s, x) => SaveToCsv(lightTime, lightData, "Light");
            btn_opendatafile.Click += (s, _) => OpenDataFolder();
            
        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {
        }

        private void label1_Click(object sender, EventArgs e)
        {
        }

        private void button1_Click(object sender, EventArgs e)
        {
                serioPort = !serioPort;
          
            if (serioPort == true)//打开串口
            {
                button1.Text = "串口已打开";
                if (COM_SELECT.SelectedItem == null ||bitspeed_select.SelectedItem == null)
                {
                    MessageBox.Show("请选择串口号和波特率");
                    return;
                }

                //与波特率，串口交互关闭
                bitspeed_select.Enabled = false;
                COM_SELECT.Enabled = false;

                //设置串口属性
                serialPort1.PortName = COM_SELECT.SelectedItem.ToString();
                serialPort1.BaudRate = int.Parse(bitspeed_select.SelectedItem.ToString());
                serialPort1.DataBits = 8;
                serialPort1.Parity = Parity.None;
                serialPort1.StopBits = StopBits.One;
                
                serialPort1.Open();

            }
            else //关闭串口
            {
                //与波特率，串口交互打开
                bitspeed_select.Enabled = true;
                COM_SELECT.Enabled = true;

                button1.Text = "串口已关闭";
                if (serialPort1.IsOpen)
                    serialPort1.Close();
            }
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox1.Checked == true) 
            { 
            checkBox2.Checked = false;
            checkBox5.Checked = false;
            }


        }

        private void checkBox2_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox2.Checked == true)
            {
                checkBox1.Checked = false;
                checkBox5.Checked = false;
            }
        }

        private void checkBox5_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox5.Checked == true)
            {
                checkBox1.Checked = false;
                checkBox2.Checked = false;
            }
        }

        private void checkBox3_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox3.Checked == true)
            {
                checkBox6.Checked = false;
                checkBox4.Checked = false;
            }

        }

        private void checkBox4_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox4.Checked == true)
            {
                checkBox6.Checked = false;
                checkBox3.Checked = false;
            }

        }

        private void checkBox6_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox6.Checked == true)
            {
                checkBox4.Checked = false;
                checkBox3.Checked = false;
            }
        }

        private void button3_Click(object sender, EventArgs e)//刷新com口
        {
            // 2. 重新扫描
            string[] ports = SerialPort.GetPortNames();

            // 3. 更新下拉框
            COM_SELECT.Items.Clear();
            COM_SELECT.Items.AddRange(ports);
            COM_SELECT.Text = "";
        }

        private void UpdateSignal(ScottPlot.Plottables.SignalXY sigXY,
                                  List<DateTime> time,
                                  List<double> value,
                                  TextBox textBox,
                                  ScottPlot.WinForms.FormsPlot plot,
                                  double newValue)
        {
            var now = DateTime.Now;
            time.Add(now);
            value.Add(newValue);

            // 同步裁剪
            if (time.Count > 1000) time.RemoveAt(0);
            if (value.Count > 1000) value.RemoveAt(0);

            // 再次确保长度一致
            int len = Math.Min(time.Count, value.Count);
            time.RemoveRange(len, time.Count - len);
            value.RemoveRange(len, value.Count - len);

            this.Invoke(new Action(() =>
            {
                // 1. 绑定数据
                sigXY.Data = new ScottPlot.DataSources.SignalXYSourceGenericArray<double, double>(
                    time.Select(t => t.ToOADate()).ToArray(),
                    value.ToArray());

                // 2. 关键：先自动缩放，再滚动
                plot.Plot.Axes.AutoScale();
                double right = DateTime.Now.ToOADate();
                double left = DateTime.Now.AddSeconds(-30).ToOADate();
                plot.Plot.Axes.SetLimitsX(left, right);

                plot.Refresh();
                textBox.Text = newValue.ToString("F2");

            }));

        }

        private void serialPort1_DataReceived(object sender, SerialDataReceivedEventArgs e)//串口数据接收   //默认温度地址为0xaa,0x55 结束为0xf0
        {
            byte checkbyte0, checkbyte1, checkbyte2;//第一个地址，第二个地址，第四个地址（第三个是数据）               //默认湿度地址为0xaa,0x99 结束为0xf0
            string checklable = "";

            byte readbyte_1byte;

            if (byte1_datareceive ==true && serioPort == true)//1字节接收
            {
                readbyte_1byte =(byte)serialPort1.ReadByte();//1010xx11

                if (datahead.Length>0 && readbyte_1byte>>(8-datahead.Length)== Convert.ToInt32(datahead, 2))//有校验帧头 且 检验校验帧头
                {

                    if (datatail.Length == 0)//无校验帧尾  1011xxxx
                    {
                        readbyte_1byte = (byte)(readbyte_1byte & ((1 << databody.Length) - 1));
                        textBox9.BeginInvoke((Action)(() =>
                        {
                            textBox9.Text = readbyte_1byte.ToString();
                        }));
                    }

                    else//有校验帧尾 10xxxx11
                    {
                        if ((byte)(readbyte_1byte & ((1 << datatail.Length) - 1)) == Convert.ToInt32(datatail, 2)) //检验校验帧尾
                        {
                            //取数据
                            readbyte_1byte = (byte)(readbyte_1byte >> datatail.Length);
                            readbyte_1byte = (byte)(readbyte_1byte & ((1 << databody.Length) - 1));

                            textBox9.BeginInvoke((Action)(() =>
                            {
                                textBox9.Text = readbyte_1byte.ToString();
                            }));

                        }
                    }
                }
                //无校验帧头
                if (datahead.Length == 0)
                {
                    if (datatail.Length == 0)//无校验帧尾时//xxxxxxxx
                    {
                        textBox9.BeginInvoke((Action)(() =>
                        {
                            textBox9.Text = readbyte_1byte.ToString();
                        }));
                    }
                    else//有校验帧尾  xxxx1010
                    {

                        readbyte_1byte = (byte)(readbyte_1byte >> datatail.Length);
                        textBox9.BeginInvoke((Action)(() =>
                        {
                            textBox9.Text = readbyte_1byte.ToString();
                        }));
                    }
                }
            }

            if (byte4_datareceive == true && serioPort == true)   //判断是否开启传感器数据接收                               //默认光照强度地址为0xaa,0xcd 结束为0xf0
            {
              

                if (checkBox8.Checked) //使用默认传输协议
                {

                    //接收数据包

                    checkbyte0 = (byte)serialPort1.ReadByte();
                    if (checkbyte0 == 0xaa)//验证数据1
                    {
                        checkbyte1 = (byte)serialPort1.ReadByte();
                        if (checkbyte1 == 0x55 || checkbyte1 == 0x99 || checkbyte1 == 0xcd)//验证数据2
                        {
                            byte_data = (byte)serialPort1.ReadByte();//接收数据

                            checkbyte2 = (byte)serialPort1.ReadByte();
                            if (checkbyte2 == 0xf0)//验证数据4
                            {
                                //校验成功
                                if (checkbyte0 == hardware1[0] && checkbyte1 == hardware1[1] && checkbyte2 == hardware1[2])
                                    checklable = "默认温度";
                                else if (checkbyte0 == hardware2[0] && checkbyte1 == hardware2[1] && checkbyte2 == hardware2[2])
                                    checklable = "默认湿度";
                                else if (checkbyte0 == hardware3[0] && checkbyte1 == hardware3[1] && checkbyte2 == hardware3[2])
                                    checklable = "默认光照";
                                else { checklable = "此数据异常"; }
                            }
                        }

                    }
                }

                 if (checkBox7.Checked)//使用自定义协议传输数据
                {
                    //接收数据包

                    checkbyte0 = (byte)serialPort1.ReadByte();
                    if (checkbyte0 == hardware_self1[0] || checkbyte0 == hardware_self2[0] || checkbyte0 == hardware_self3[0])//验证数据1
                    {
                        checkbyte1 = (byte)serialPort1.ReadByte();
                        if (checkbyte1 == hardware_self1[1] || checkbyte1 == hardware_self2[1] || checkbyte1 == hardware_self3[1])//验证数据2
                        {
                            byte_data = (byte)serialPort1.ReadByte();//接收数据

                            checkbyte2 = (byte)serialPort1.ReadByte();
                            if (checkbyte2 == hardware_self1[2] || checkbyte2 == hardware_self2[2] || checkbyte2 == hardware_self3[2])//验证数据4
                            {
                                //校验成功
                                if (checkbyte0 == hardware_self1[0] && checkbyte1 == hardware_self1[1] && checkbyte2 == hardware_self1[2])
                                    checklable = "自定义温度";
                                else if (checkbyte0 == hardware_self2[0] && checkbyte1 == hardware_self2[1] && checkbyte2 == hardware_self2[2])
                                    checklable = "自定义湿度";
                                else if (checkbyte0 == hardware_self3[0] && checkbyte1 == hardware_self3[1] && checkbyte2 == hardware_self3[2])
                                    checklable = "自定义光照";
                                else { checklable = "此数据异常"; }
                            }
                        }

                    }
                }

                if (checklable == "默认温度" || checklable == "自定义温度")
                {
                    data_temp = byte_data;
                    if (tempData.Count == 1 && tempData[0] == 0) tempData[0] = data_temp;
                    else tempData.Add(data_temp);
                    UpdateSignal(sigTemp, tempTime, tempData, textBox1, formsPlot1, data_temp);
                }


                if (checklable == "默认湿度" || checklable == "自定义湿度")
                {
                    data_humi = byte_data;
                    if (humiData.Count == 1 && humiData[0] == 0) humiData[0] = data_humi;
                    else humiData.Add(data_humi);
                    UpdateSignal(sigHumi, humiTime, humiData, textBox2, formsPlot2, data_humi);
                }


                if (checklable == "默认光照" || checklable == "自定义光照")
                {
                    data_light = byte_data;
                    if (lightData.Count == 1 && lightData[0] == 0) lightData[0] = data_light;
                    else lightData.Add(data_light);
                    UpdateSignal(sigLight, lightTime, lightData, textBox3, formsPlot3, data_light);
                }

            }//4字节接收

            if (checkBox5.Checked&&button6.Text=="数据接收已关闭")//选中文本接收
            {
                string data = serialPort1.ReadExisting();   // 或 ReadLine()/Read(bytes...)
                                                            // 跨线程安全地更新富文本框
                this.BeginInvoke(new Action(() =>
                {
                    richTextBoxRx.AppendText(data);   // 追加文本
                    richTextBoxRx.ScrollToCaret();    // 自动滚到最后一行
                }));
            }

            if (checkBox1.Checked && button6.Text == "数据接收已关闭")//选中16进制接收  0-0xff
            {
                int bytes = serialPort1.BytesToRead;
                if (bytes == 0) return;

                byte[] buf = new byte[bytes];
                serialPort1.Read(buf, 0, bytes);           // 读所有字节

                // 每个字节转两位 HEX
                string hex = string.Join(" ",
                             buf.Select(b => b.ToString("X2")));

                // 追加到 RichTextBox
                this.BeginInvoke(new Action(() =>
                {
                    richTextBoxRx.AppendText("0x"+hex + '\n');
                    richTextBoxRx.ScrollToCaret();
                }));
                
            }

            if (checkBox2.Checked && button6.Text == "数据接收已关闭")//选10进制接收   0-255
            {
                if (serialPort1.BytesToRead == 0) return;

                byte oneByte = (byte)serialPort1.ReadByte();   // 只读 1 字节
                string dec = oneByte.ToString();               // 0-255 的十进制

                // 追加到 RichTextBox
                this.BeginInvoke(new Action(() =>
                {
                    richTextBoxRx.AppendText(dec + '\n');
                    richTextBoxRx.ScrollToCaret();
                }));
            }

        }

        private void button4_Click(object sender, EventArgs e)//清除接收内容
        {
            richTextBoxRx.Clear();
        }

        private void button5_Click(object sender, EventArgs e)//清除发送内容
        {
            richTextBoxTx.Clear();
        }

        private void button2_Click(object sender, EventArgs e)//发送富文本框内容
        {
            if (checkBox3.Checked)//16进制发送
            {
                if (!serialPort1.IsOpen) { MessageBox.Show("串口未打开"); return; }
                string hex = richTextBoxTx.Text.Replace(" ", "").Replace("\r", "").Replace("\n", "").Replace("\t", "").ToUpper();
                if (hex.Length % 2 != 0) { MessageBox.Show("16进制长度必须为偶数"); return; }

                try
                {
                    byte[] buffer = Enumerable.Range(0, hex.Length / 2)
                                              .Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16))
                                              .ToArray();
                    serialPort1.Write(buffer, 0, buffer.Length);
                }
                catch (Exception ex) { MessageBox.Show("发送失败：" + ex.Message); }
            }
            if (checkBox4.Checked)//10进制发送
            {
                if (!serialPort1.IsOpen) { MessageBox.Show("串口未打开"); return; }
                string[] parts = richTextBoxTx.Text.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                try
                {
                    byte[] buffer = parts.Select(s => Convert.ToByte(s)).ToArray(); // 0-255
                    serialPort1.Write(buffer, 0, buffer.Length);
                }
                catch (Exception ex) { MessageBox.Show("发送失败：" + ex.Message); }
            }

            if (checkBox6.Checked)//文本发送
            {
                if (!serialPort1.IsOpen) { MessageBox.Show("串口未打开"); return; }
                try
                {
                    serialPort1.Write(richTextBoxTx.Text);   // 默认按 Encoding.ASCII/UTF8
                }
                catch (Exception ex) { MessageBox.Show("发送失败：" + ex.Message); }
            }


        }

        private void 帮助ToolStripMenuItem_Click(object sender, EventArgs e)
        {

        }

        private void 使用说明ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MessageBox.Show("作为串口通信：\n选择波特率与下位机波特率一致，选择设备com口,打开串口。\n作为传感器数据传输：\n选择波特率与下位机波特率一致，选择设备com口,打开串口,打开数据接收");
        }

        private void 支持作者ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            
        }

        private void 上下位机通信说明ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MessageBox.Show("下位机发送数据格式："+'\n'+"温度：0xaa 0x55 data 0xf0" + '\n'+ "湿度：0xaa 0x99 data 0xf0" + '\n' + "光照强度：0xaa 0xcd data 0xf0");

        }

        private void label12_Click(object sender, EventArgs e)
        {

        }

        private void comboBox2_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void checkBox8_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox8.Checked == true)
            {
                checkBox7.Enabled = false;

                button7.Enabled = false;

                textBox4.ReadOnly = true;
                textBox5.ReadOnly = true;
                textBox7.ReadOnly = true;

                if (comboBox2.Text == "温度")
                {
                    textBox4.Text = "0x" + hardware1[0].ToString("X2");//转位16进制
                    textBox5.Text = "0x" + hardware1[1].ToString("X2");
                    textBox7.Text = "0x" + hardware1[2].ToString("X2");
                }

                if (comboBox2.Text == "湿度")
                {
                    textBox4.Text = "0x" + hardware2[0].ToString("X2");
                    textBox5.Text = "0x" + hardware2[1].ToString("X2");
                    textBox7.Text = "0x" + hardware2[2].ToString("X2");
                }

                if (comboBox2.Text == "光照")
                {
                    textBox4.Text = "0x" + hardware3[0].ToString("X2");
                    textBox5.Text = "0x" + hardware3[1].ToString("X2");
                    textBox7.Text = "0x" + hardware3[2].ToString("X2");
                }
            }

            else
            {
                checkBox7.Enabled = true;
                checkBox8.Enabled = false;
                checkBox7.Checked = true;
            }

        }

        private void comboBox2_SelectedIndexChanged_1(object sender, EventArgs e)
        {


            if (checkBox8.Checked == true)
            {
                    if (comboBox2.Text == "温度")
                    {
                        textBox4.Text = "0x" + hardware1[0].ToString("X2");//转位16进制
                        textBox5.Text = "0x" + hardware1[1].ToString("X2");
                        textBox7.Text = "0x" + hardware1[2].ToString("X2");
                    }

                    if (comboBox2.Text == "湿度")
                    {
                        textBox4.Text = "0x" + hardware2[0].ToString("X2");
                        textBox5.Text = "0x" + hardware2[1].ToString("X2");
                        textBox7.Text = "0x" + hardware2[2].ToString("X2");
                    }

                    if (comboBox2.Text == "光照")
                    {
                        textBox4.Text = "0x" + hardware3[0].ToString("X2");
                        textBox5.Text = "0x" + hardware3[1].ToString("X2");
                        textBox7.Text = "0x" + hardware3[2].ToString("X2");
                    }

            }


            if (checkBox7.Checked == true)  //自定义协议
            {
                if (comboBox2.Text == "温度")
                {
                    textBox4.Text = "0x" + hardware_self1[0].ToString("X2");//转位16进制
                    textBox5.Text = "0x" + hardware_self1[1].ToString("X2");
                    textBox7.Text = "0x" + hardware_self1[2].ToString("X2");
                    button7.Text = "保存温度类型的协议";
                }

                if (comboBox2.Text == "湿度")
                {
                    textBox4.Text = "0x" + hardware_self2[0].ToString("X2");//转位16进制
                    textBox5.Text = "0x" + hardware_self2[1].ToString("X2");
                    textBox7.Text = "0x" + hardware_self2[2].ToString("X2");
                    button7.Text = "保存湿度类型的协议";
                }

                if (comboBox2.Text == "光照")
                {
                    textBox4.Text = "0x" + hardware_self3[0].ToString("X2");//转位16进制
                    textBox5.Text = "0x" + hardware_self3[1].ToString("X2");
                    textBox7.Text = "0x" + hardware_self3[2].ToString("X2");
                    button7.Text = "保存光照类型的协议";
                }
            }

        }

        private void checkBox7_CheckedChanged(object sender, EventArgs e)
        {

            if (!checkBox7.Checked)
            {
                checkBox8.Checked = true;
                checkBox7.Enabled = false;
                checkBox8.Enabled = true;
            }

            if (checkBox7.Checked)
            {
                button7.Enabled = true;

                textBox4.ReadOnly = false;
                textBox5.ReadOnly = false;
                textBox7.ReadOnly = false;

                textBox4.Text = "";
                textBox5.Text = "";
                textBox7.Text = "";
            }


        }

        private void button7_Click(object sender, EventArgs e)
        {
            if (textBox4.Text =="" || textBox5.Text =="" || textBox7.Text =="")
            {
                MessageBox.Show("数值不能为空！");
                return;
            }


            if (comboBox2.Text == "温度")
            {
                try {
                    hardware_self1[0] = Convert.ToByte(textBox4.Text, 16);   //字符串转化为16进制
                    hardware_self1[1] = Convert.ToByte(textBox5.Text, 16);   //字符串转化为16进制
                    hardware_self1[2] = Convert.ToByte(textBox7.Text, 16);   //字符串转化为16进制
                }
                catch(FormatException)
                {
                    MessageBox.Show("格式错误，请直接输入如 0xf0 0XF0格式，或直接输入f0或F0样式格式");
                    return;
                }
                catch (OverflowException)
                {
                    MessageBox.Show("数字大小应在0X00-0XFF内，即1个字节");
                }
                catch (Exception ex)   // 兜底
                {
                    MessageBox.Show("未知错误：" + ex.Message);
                }

            }

            if (comboBox2.Text == "湿度")
            {
                try
                {
                    hardware_self2[0] = Convert.ToByte(textBox4.Text, 16);   //字符串转化为16进制
                    hardware_self2[1] = Convert.ToByte(textBox5.Text, 16);   //字符串转化为16进制
                    hardware_self2[2] = Convert.ToByte(textBox7.Text, 16);   //字符串转化为16进制 
                }

                catch (FormatException)
                {
                    MessageBox.Show("格式错误，请直接输入如 0xf0 0XF0格式，或直接输入f0或F0样式格式");
                    return;
                }
                catch (OverflowException)
                {
                    MessageBox.Show("数字大小应在0X00-0XFF内，即1个字节");
                }
                catch (Exception ex)   // 兜底
                {
                    MessageBox.Show("未知错误：" + ex.Message);
                }
            }

            if (comboBox2.Text == "光照")
            {
                try
                {
                    hardware_self3[0] = Convert.ToByte(textBox4.Text, 16);   //字符串转化为16进制
                    hardware_self3[1] = Convert.ToByte(textBox5.Text, 16);   //字符串转化为16进制
                    hardware_self3[2] = Convert.ToByte(textBox7.Text, 16);   //字符串转化为16进制
                }
                catch (FormatException)
                {
                    MessageBox.Show("格式错误，请直接输入如 0xf0 0XF0格式，或直接输入f0或F0样式格式");
                    return;
                }
                catch (OverflowException)
                {
                    MessageBox.Show("数字大小应在0X00-0XFF内，即1个字节(10进制0-255)");
                }
                catch (Exception ex)   // 兜底
                {
                    MessageBox.Show("未知错误：" + ex.Message);
                }
            }
        }

        private void textBox4_TextChanged(object sender, EventArgs e)
        {
        }

        private void textBox1_TextChanged_1(object sender, EventArgs e)
        {

        }

        private void 关于此软件ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MessageBox.Show("我的第一个软件，可能有bug，有问题联系我qq: 3326799274");
        }

        private void button6_Click(object sender, EventArgs e)
        {

            byte4_datareceive = !byte4_datareceive;
            if (byte4_datareceive == true&& serioPort==true)
            {
                button6.Text = "数据接收已开启";
                checkBox1.Enabled = false;
                checkBox2.Enabled = false;
                checkBox5.Enabled = false;
                button1.Enabled = false;
                button4.Enabled = false;
                button14.Enabled = false;
            }

            if (byte4_datareceive == false)
            {
                checkBox1.Enabled = true;
                checkBox2.Enabled = true;
                checkBox5.Enabled = true;

                textBox1.Text = "";
                textBox2.Text = "";
                textBox3.Text = "";

                button14.Enabled = true;
            }


            if (byte4_datareceive == false || serioPort == false)
            {
                button6.Text = "数据接收已关闭";
                button1.Enabled = true;
                button4.Enabled = true;
                
            }

        }

        private void button8_Click(object sender, EventArgs e)
        {
            if(checkBox8.Checked)
            {
                MessageBox.Show("当前协议：默认传输协议\n\n"+ 
                                "-------温度协议-------" + '\n' + "校准byte1: 0xaa" + '\n' + "校准byte2: 0x55" + '\n' + "DATA" + '\n' + "校准byte4: 0xf0\n\n" +
                                "-------湿度协议-------" + '\n' + "校准byte1: 0xaa" + '\n' + "校准byte2: 0x99" + '\n' + "DATA" + '\n' + "校准byte4: 0xf0\n\n" +
                                "-------光照协议-------" + '\n' + "校准byte1: 0xaa" + '\n' + "校准byte2: 0xcd" + '\n' + "DATA" + '\n' + "校准byte4: 0xf0\n\n"  );
            }

            if (checkBox7.Checked)
            {
                MessageBox.Show("当前协议：自定义传输协议\n\n" +
                               "-------温度协议-------" + '\n' + "校准byte1:" + "0X" + hardware_self1[0].ToString("X2") + '\n' + "校准byte2:" + "0X" + hardware_self1[1].ToString("X2") + '\n' + "DATA" + '\n' + "校准byte4:" + "0X" + hardware_self1[2].ToString("X2") + "\n\n" +
                               "-------湿度协议-------" + '\n' + "校准byte1:" + "0X" + hardware_self2[0].ToString("X2") + '\n' + "校准byte2:" + "0X" + hardware_self2[1].ToString("X2") + '\n' + "DATA" + '\n' + "校准byte4:" + "0X" + hardware_self2[2].ToString("X2") + "\n\n" +
                               "-------光照协议-------" + '\n' + "校准byte1:" + "0X" + hardware_self3[0].ToString("X2") + '\n' + "校准byte2:" + "0X" + hardware_self3[1].ToString("X2") + '\n' + "DATA" + '\n' + "校准byte4:" + "0X" + hardware_self3[2].ToString("X2") + "\n\n"  );
            }
        }

        private void label8_Click(object sender, EventArgs e)
        {

        }

        private void 说明ToolStripMenuItem_Click(object sender, EventArgs e)
        {

        }

        private void 通信格式说明ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MessageBox.Show("4byte传输中，校准byte数值范围在0x00-0xff，即一个字节，1byte传输中，共8位，必须有表示数据的一段x，且x个数不为0.");
        }

        private void formsPlot2_Load(object sender, EventArgs e)//湿度
        {

        }

        private void formsPlot1_Load(object sender, EventArgs e)//温度
        {

        }

        private void formsPlot3_Load(object sender, EventArgs e)//光照强度
        {

        }

        private void button12_Click(object sender, EventArgs e)
        {
            // 清空数据缓存
            tempTime.Clear(); tempData.Clear();
            humiTime.Clear(); humiData.Clear();
            lightTime.Clear(); lightData.Clear();

            // 给 SignalXY 塞一个占位点，防止空图
            var now = DateTime.Now;
            tempTime.Add(now); tempData.Add(0);
            humiTime.Add(now); humiData.Add(0);
            lightTime.Add(now); lightData.Add(0);

            // 重新绑定占位数据
            sigTemp.Data = new ScottPlot.DataSources.SignalXYSourceGenericArray<double, double>(
                tempTime.Select(t => t.ToOADate()).ToArray(), tempData.ToArray());
            sigHumi.Data = new ScottPlot.DataSources.SignalXYSourceGenericArray<double, double>(
                humiTime.Select(t => t.ToOADate()).ToArray(), humiData.ToArray());
            sigLight.Data = new ScottPlot.DataSources.SignalXYSourceGenericArray<double, double>(
                lightTime.Select(t => t.ToOADate()).ToArray(), lightData.ToArray());

            // 自动缩放到新占位点
            formsPlot1.Plot.Axes.AutoScale();
            formsPlot2.Plot.Axes.AutoScale();
            formsPlot3.Plot.Axes.AutoScale();

            // 刷新
            formsPlot1.Refresh();
            formsPlot2.Refresh();
            formsPlot3.Refresh();
        }

        private void 曲线使用说明ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MessageBox.Show("选择波特率和com口，打开串口，打开4byte中的数据接收按钮，按照上下位机通信格式由下位机向上位机发送数据，即可得到曲线，1byte中显示数据曲线的功能还没做");
        }

        private void formsPlot1_Load_1(object sender, EventArgs e)
        {

        }

        private void buttonTest_Click_1(object sender, EventArgs e)
        {

        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            // 停止数据接收和关闭串口
            byte4_datareceive = false;
            byte1_datareceive = false;
            if (serialPort1.IsOpen)
            {
                serialPort1.Close();
            }

        }

        private void button9_Click(object sender, EventArgs e)
        {
            tempTime.Clear();
            tempData.Clear();
            // 添加一个占位点，防止空图
            var now = DateTime.Now;
            tempTime.Add(now);
            tempData.Add(0);

            // 更新温度曲线数据
            sigTemp.Data = new ScottPlot.DataSources.SignalXYSourceGenericArray<double, double>(
                tempTime.Select(t => t.ToOADate()).ToArray(),
                tempData.ToArray());
            formsPlot1.Plot.Axes.AutoScale();
            formsPlot1.Plot.Axes.SetLimitsX(now.AddSeconds(-30).ToOADate(), now.ToOADate());
            formsPlot1.Refresh();
        }

        private void button10_Click(object sender, EventArgs e)
        {
            humiTime.Clear();
            humiData.Clear();
            // 添加一个占位点，防止空图
            var now = DateTime.Now;
            humiTime.Add(now);
            humiData.Add(0);

            // 更新湿度曲线数据
            sigHumi.Data = new ScottPlot.DataSources.SignalXYSourceGenericArray<double, double>(
                humiTime.Select(t => t.ToOADate()).ToArray(),
                humiData.ToArray());
            formsPlot2.Plot.Axes.AutoScale();
            formsPlot2.Plot.Axes.SetLimitsX(now.AddSeconds(-30).ToOADate(), now.ToOADate());
            formsPlot2.Refresh();
        }

        private void 支持一杯柠檬水ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start("https://m.tb.cn/h.hst57su?tk=ABbz48L0j41");
        }

        private void comboBox3_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void groupBox6_Enter(object sender, EventArgs e)
        {

        }

        private void button16_Click(object sender, EventArgs e)
        {
            int x_num = 0;
            datahead = "";
            databody = "";
            datatail = "";
            if (textBox8.Text.Length!=8)
            {
                MessageBox.Show("请输入正确格式并保存。");
                textBox8.Text = "";
                return ;    
            }

            foreach (char c in textBox8.Text)
            {
                if (!(c == 'x' || c == 'X' || c == '0' || c == '1'))
                {
                    MessageBox.Show("请输入正确格式并保存。");
                    textBox8.Text = "";
                    datahead = "";
                    databody = "";
                    datatail = "";
                    return;
                }
                if ((c == '0' || c == '1')&&(x_num==0))//接收校验帧头
                {
                    datahead += c;
                }

                if ((c == '0' || c == '1') && (x_num > 0))//接收校验帧尾
                {
                    datatail += c;
                }

                if (c == 'x' || c == 'X') //接收数据帧
                {
                    databody += 'x';
                    x_num++;

                }
                if (x_num == 0 && (datahead.Length + databody.Length + datatail.Length == 8))
                {
                    MessageBox.Show("数据位至少为1位,请输入正确格式并保存");
                    textBox8.Text = "";
                    datahead = "";
                    databody = "";
                    datatail = "";
                    return;
                }
                if (datahead.Length + databody.Length + datatail.Length == 8)
                {
                    //trueinput = datahead + databody + datatail;
                    MessageBox.Show("已成功保存。"+ "\n校验帧头："+datahead+ "\n数据位：" + databody + "\n校验帧尾：" + datatail);
                    //MessageBox.Show("head " + datahead.Length + '\n' + "body " + databody.Length + '\n' + "tail " + datatail.Length + '\n');
                    x_num = 0;
                }

            }

        }

        private void button13_Click(object sender, EventArgs e)
        {
            MessageBox.Show("校验位：勾选\n数据位:   不勾选");
        }

        private void button17_Click(object sender, EventArgs e)
        {
            MessageBox.Show("总长度为8位，校验为1或0，数据为大写或小写X\n其中x必须连续，且只有一段X, X数量不可为0\n如：101XXX10");
        }

        private void label17_Click(object sender, EventArgs e)
        {

        }

        private void button13_Click_1(object sender, EventArgs e)
        {
            if (datahead.Length + databody.Length + datatail.Length == 8)
            {
                MessageBox.Show("当前格式为" + datahead + databody + datatail);
            }
            else { MessageBox.Show("请输入正确格式并保存。"); }
        }

        private void button14_Click(object sender, EventArgs e)
        {
            byte1_datareceive = !byte1_datareceive;
            if (byte1_datareceive == true && serioPort == true)
            {
                if (datahead.Length + databody.Length + datatail.Length != 8)
                {
                    MessageBox.Show("请输入正确格式并保存。");
                    return; } 

                button14.Text = "数据接收已开启";
                button6.Enabled = false;
                checkBox1.Enabled = false;
                checkBox2.Enabled = false;
                checkBox5.Enabled = false;
                button1.Enabled = false;
                button4.Enabled = false;

            }
            if (byte1_datareceive == false)    
            {
                button14.Text = "数据接收已关闭";
                button6.Enabled = true;
                checkBox1.Enabled = true;
                checkBox2.Enabled = true;
                checkBox5.Enabled = true;
                
            }

            if (serioPort == false || byte1_datareceive == false )
            {
                button14.Text = "数据接收已关闭";
                button1.Enabled = true;
                button4.Enabled = true;

            }


        }

        private void richTextBoxRx_TextChanged(object sender, EventArgs e)
        {

        }

        private void textBox3_TextChanged(object sender, EventArgs e)
        {

        }

        private void button15_Click(object sender, EventArgs e)
        {

        }

        private void textBox8_TextChanged(object sender, EventArgs e)
        {

        }

        private void button11_Click(object sender, EventArgs e)
        {
            lightTime.Clear();
            lightData.Clear();
            // 添加一个占位点，防止空图
            var now = DateTime.Now;
            lightTime.Add(now);
            lightData.Add(0);

            // 更新光照曲线数据
            sigLight.Data = new ScottPlot.DataSources.SignalXYSourceGenericArray<double, double>(
                lightTime.Select(t => t.ToOADate()).ToArray(),
                lightData.ToArray());
            formsPlot3.Plot.Axes.AutoScale();
            formsPlot3.Plot.Axes.SetLimitsX(now.AddSeconds(-30).ToOADate(), now.ToOADate());
            formsPlot3.Refresh();
        }

        private void label4_Click(object sender, EventArgs e)
        {

        }

    }
}
