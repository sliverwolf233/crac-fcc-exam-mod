// CRAC/FCC 模拟考试 单文件启动器（BI2XM 魔改版）
// 内嵌全部程序与题库；隐私数据外置于 个人数据.dat；本源码不含任何反斜杠字面量
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows.Forms;

namespace CRACLauncher
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (args != null && args.Length > 0 && args[0] == "--extract-only")
            {
                Engine.Prepare();
                Console.WriteLine("extracted");
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    static class Engine
    {
        public static string BaseDir;
        public static string WorkDir;
        public static string BanksDir;
        public static string DataFile;

        static Engine() { InitDirs(); }

        public static void InitDirs()
        {
            BaseDir = AppDomain.CurrentDomain.BaseDirectory;
            WorkDir = Path.Combine(BaseDir, "sim_app");
            BanksDir = Path.Combine(WorkDir, "banks");
            DataFile = Path.Combine(BaseDir, "personal.dat");
        }

        static readonly string[] Payloads = new string[] {
            "Simulation.exe", "SimPatches.dll", "LiveCharts.DLL", "LiveCharts.Wpf.DLL",
            "Newtonsoft.Json.DLL", "SQLite.Interop.dll", "System.Data.SQLite.DLL", "Simulation.exe.config"
        };
        static readonly string[] BankPayloads = new string[] { "CRAC.sqlite3", "FAA.sqlite3", "FAA_BI.sqlite3" };

        public static string DQ1 { get { return new string((char)34, 1); } }
        public static string BS1 { get { return new string((char)92, 1); } }

        public static void ExtractResource(string resName, string destFile)
        {
            using (var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("payload." + resName + ".gz"))
            {
                if (s == null) throw new Exception("missing embedded resource: " + resName);
                using (var gz = new GZipStream(s, CompressionMode.Decompress))
                using (var outF = File.Create(destFile))
                {
                    byte[] buf = new byte[65536];
                    int n;
                    while ((n = gz.Read(buf, 0, buf.Length)) > 0) outF.Write(buf, 0, n);
                }
            }
        }

        const string PayloadVersion = "v3";

        public static void Prepare()
        {
            Directory.CreateDirectory(WorkDir);
            Directory.CreateDirectory(BanksDir);
            string marker = Path.Combine(WorkDir, ".extracted");
            string verFile = Path.Combine(WorkDir, ".version");
            string curVer = File.Exists(verFile) ? File.ReadAllText(verFile, Encoding.UTF8).Trim() : "";
            if (!File.Exists(marker) || curVer != PayloadVersion)
            {
                foreach (string f in Payloads) ExtractResource(f, Path.Combine(WorkDir, f));
                foreach (string b in BankPayloads)
                {
                    string dst = Path.Combine(BanksDir, b);
                    if (!File.Exists(dst)) ExtractResource(b, dst);
                }
                File.WriteAllText(marker, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Encoding.UTF8);
                File.WriteAllText(verFile, PayloadVersion, Encoding.UTF8);
                // 启动器自身需要 SQLite（进度迁移），把两个 DLL 复制到启动器目录
                foreach (string dll in new string[] { "System.Data.SQLite.DLL", "SQLite.Interop.dll" })
                {
                    string to = Path.Combine(BaseDir, dll);
                    if (!File.Exists(to)) File.Copy(Path.Combine(WorkDir, dll), to);
                }
            }
            if (!File.Exists(ActiveFile)) File.WriteAllText(ActiveFile, "CRAC", Encoding.UTF8);
            EnsureActiveDb();
        }

        static string ActiveFile { get { return Path.Combine(BanksDir, "active.txt"); } }
        public static bool HasDataFile { get { return File.Exists(DataFile); } }

        static string BilingualFile { get { return Path.Combine(BanksDir, "bilingual.txt"); } }
        public static bool Bilingual
        {
            get { return !File.Exists(BilingualFile) || File.ReadAllText(BilingualFile, Encoding.UTF8).Trim() == "1"; }
            set { File.WriteAllText(BilingualFile, value ? "1" : "0", Encoding.UTF8); }
        }
        static string EffBank(string bank) { return (bank == "FAA" && Bilingual) ? "FAA_BI" : bank; }

        public static void ToggleBilingual(bool on)
        {
            bool old = Bilingual;
            if (old == on) return;
            string effOld = EffBank(ActiveBank);
            Bilingual = on;
            string effNew = EffBank(ActiveBank);
            if (effOld == effNew) return;
            try
            {
                EnsureActiveDb();
                File.Copy(ActiveDb, BankPath(effOld), true);
                MigrateUsers(BankPath(effOld), BankPath(effNew));
                File.Copy(BankPath(effNew), ActiveDb, true);
            }
            catch { }
        }

        static void MigrateUsers(string srcDb, string dstDb)
        {
            using (var dst = new System.Data.SQLite.SQLiteConnection("Data Source=" + dstDb + ";Version=3;"))
            {
                dst.Open();
                using (var cmd = dst.CreateCommand())
                {
                    cmd.CommandText = "ATTACH DATABASE '" + srcDb + "' AS srcdb;";
                    cmd.ExecuteNonQuery();
                    string[] tables = new string[] { "Examinee", "Learn", "LearnList", "Mistake", "Simulation", "SimulationList", "Collection" };
                    foreach (string t in tables)
                    {
                        cmd.CommandText = "DELETE FROM " + t + ";";
                        cmd.ExecuteNonQuery();
                        cmd.CommandText = "INSERT INTO " + t + " SELECT * FROM srcdb." + t + ";";
                        cmd.ExecuteNonQuery();
                    }
                    cmd.CommandText = "DETACH DATABASE srcdb;";
                    cmd.ExecuteNonQuery();
                }
                dst.Close();
            }
        }

        public static string ActiveBank
        {
            get { return File.Exists(ActiveFile) ? File.ReadAllText(ActiveFile, Encoding.UTF8).Trim() : "CRAC"; }
        }

        static string ActiveDb { get { return Path.Combine(WorkDir, "SimulationSys.sqlite3"); } }
        static string BankPath(string bank) { return Path.Combine(BanksDir, bank + ".sqlite3"); }

        public static void EnsureActiveDb()
        {
            if (File.Exists(ActiveDb)) return;
            string src = BankPath(ActiveBank);
            if (!File.Exists(src)) src = BankPath("CRAC");
            if (File.Exists(src)) File.Copy(src, ActiveDb, true);
        }

        const string Marker = "===DATA===";

        public static void RestorePrivacy()
        {
            try
            {
                if (!File.Exists(DataFile)) return;
                byte[] all = File.ReadAllBytes(DataFile);
                using (var ms = new MemoryStream())
                {
                    using (var fs = new MemoryStream(all))
                    using (var gz = new GZipStream(fs, CompressionMode.Decompress))
                    {
                        byte[] buf = new byte[65536]; int n;
                        while ((n = gz.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
                    }
                    byte[] dec = ms.ToArray();
                    byte[] mark = Encoding.UTF8.GetBytes(Marker);
                    int binIdx = IndexOfBytes(dec, mark, 0);
                    if (binIdx < 0) return;
                    string meta = Encoding.UTF8.GetString(dec, 0, binIdx);
                    byte[] dbBytes = new byte[dec.Length - binIdx - mark.Length];
                    Array.Copy(dec, binIdx + mark.Length, dbBytes, 0, dbBytes.Length);
                    File.WriteAllBytes(ActiveDb, dbBytes);
                    PatchConfig(ExtractJsonField(meta, "name"), ExtractJsonField(meta, "id"));
                }
            }
            catch { }
        }

        public static void SavePrivacy()
        {
            try
            {
                if (!File.Exists(ActiveDb)) return;
                string name, id;
                ReadConfig(out name, out id);
                string dq = DQ1;
                StringBuilder meta = new StringBuilder();
                meta.Append("{").Append(dq).Append("name").Append(dq).Append(":").Append(dq).Append(Escape(name)).Append(dq).Append(",")
                    .Append(dq).Append("id").Append(dq).Append(":").Append(dq).Append(Escape(id)).Append(dq).Append(",")
                    .Append(dq).Append("bank").Append(dq).Append(":").Append(dq).Append(ActiveBank).Append(dq).Append(",")
                    .Append(dq).Append("time").Append(dq).Append(":").Append(dq).Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append(dq).Append("}");
                byte[] metaB = Encoding.UTF8.GetBytes(meta.ToString());
                byte[] markB = Encoding.UTF8.GetBytes(Marker);
                byte[] dbB = File.ReadAllBytes(ActiveDb);
                using (var fs = File.Create(DataFile))
                using (var gz = new GZipStream(fs, CompressionMode.Compress))
                {
                    gz.Write(metaB, 0, metaB.Length);
                    gz.Write(markB, 0, markB.Length);
                    gz.Write(dbB, 0, dbB.Length);
                }
                PatchConfig("", "");
                TryDelete(ActiveDb + "-journal"); TryDelete(ActiveDb + "-wal"); TryDelete(ActiveDb + "-shm");
                TryDelete(ActiveDb);
            }
            catch { }
        }

        static void TryDelete(string f) { try { if (File.Exists(f)) File.Delete(f); } catch { } }

        static string Escape(string s)
        {
            return (s ?? "").Replace(BS1, BS1 + BS1).Replace(DQ1, BS1 + DQ1);
        }

        static string ExtractJsonField(string json, string key)
        {
            string pat = DQ1 + key + DQ1 + ":" + DQ1;
            int i = json.IndexOf(pat, StringComparison.Ordinal);
            if (i < 0) return "";
            int start = i + pat.Length;
            int end = json.IndexOf(DQ1, start, StringComparison.Ordinal);
            if (end < 0) return "";
            return json.Substring(start, end - start).Replace(BS1 + DQ1, DQ1).Replace(BS1 + BS1, BS1);
        }

        static string ConfigPath { get { return Path.Combine(WorkDir, "Simulation.exe.config"); } }

        public static void PatchConfig(string name, string id)
        {
            try
            {
                if (!File.Exists(ConfigPath)) return;
                string c = File.ReadAllText(ConfigPath, Encoding.UTF8);
                c = ReplaceKey(c, "UserName", name);
                c = ReplaceKey(c, "IDCardNo", id);
                File.WriteAllText(ConfigPath, c, Encoding.UTF8);
            }
            catch { }
        }

        static string ReplaceKey(string xml, string key, string value)
        {
            string pat = "key=" + DQ1 + key + DQ1 + " value=" + DQ1;
            int i = xml.IndexOf(pat, StringComparison.Ordinal);
            if (i < 0) return xml;
            int start = i + pat.Length;
            int end = xml.IndexOf((char)34, start);
            if (end < 0) return xml;
            return xml.Substring(0, start) + (value ?? "").Replace(DQ1, "&quot;") + xml.Substring(end);
        }

        public static void ReadConfig(out string name, out string id)
        {
            name = ""; id = "";
            try
            {
                if (!File.Exists(ConfigPath)) return;
                string c = File.ReadAllText(ConfigPath, Encoding.UTF8);
                name = ExtractXmlValue(c, "UserName");
                id = ExtractXmlValue(c, "IDCardNo");
            }
            catch { }
        }

        static string ExtractXmlValue(string xml, string key)
        {
            string pat = "key=" + DQ1 + key + DQ1 + " value=" + DQ1;
            int i = xml.IndexOf(pat, StringComparison.Ordinal);
            if (i < 0) return "";
            int start = i + pat.Length;
            int end = xml.IndexOf((char)34, start);
            if (end < 0) return "";
            return xml.Substring(start, end - start);
        }

        public static void SwitchBank(string target)
        {
            string effNew = EffBank(target);
            string effOld = EffBank(ActiveBank);
            if (effNew == effOld && File.Exists(ActiveDb) && target == ActiveBank) return;
            if (File.Exists(ActiveDb))
            {
                File.Copy(ActiveDb, BankPath(effOld), true);
            }
            TryDelete(ActiveDb + "-journal"); TryDelete(ActiveDb + "-wal"); TryDelete(ActiveDb + "-shm");
            File.Copy(BankPath(effNew), ActiveDb, true);
            File.WriteAllText(ActiveFile, target, Encoding.UTF8);
        }

        public static string ImportBank(string srcPath)
        {
            string name = Path.GetFileNameWithoutExtension(srcPath);
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            File.Copy(srcPath, BankPath(name), true);
            return name;
        }

        static int IndexOfBytes(byte[] hay, byte[] needle, int start)
        {
            for (int i = start; i <= hay.Length - needle.Length; i++)
            {
                bool ok = true;
                for (int j = 0; j < needle.Length; j++) { if (hay[i + j] != needle[j]) { ok = false; break; } }
                if (ok) return i;
            }
            return -1;
        }

        public static Process LaunchApp()
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = Path.Combine(WorkDir, "Simulation.exe");
            psi.WorkingDirectory = WorkDir;
            return Process.Start(psi);
        }
    }

    class MainForm : Form
    {
        Button btnCRAC, btnFAA, btnImport, btnStart;
        CheckBox ckRemember, ckBilingual;
        Label labStatus, labBank, labPrivacy;
        string imported = null;

        public MainForm()
        {
            Text = "CRAC/FCC 模拟考试系统 BI2XM 魔改版 启动器";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Width = 580; Height = 330;

            Label title = new Label();
            title.Text = "选择题库并启动（仅供学习使用 严禁售卖）";
            title.Location = new System.Drawing.Point(20, 15);
            title.AutoSize = true;
            Controls.Add(title);

            btnCRAC = MkBtn("中国大陆 CRAC 2025", 20, 50, 250, 60);
            btnFAA = MkBtn("美国 FCC（T级 中英双语）", 300, 50, 250, 60);
            btnCRAC.Click += delegate { Pick("CRAC"); };
            btnFAA.Click += delegate { Pick("FAA"); };

            ckBilingual = new CheckBox();
            ckBilingual.Text = "FCC 题库显示中文翻译（中英双语，T级全部/G、E级部分）";
            ckBilingual.Location = new System.Drawing.Point(20, 122);
            ckBilingual.AutoSize = true;
            ckBilingual.Checked = Engine.Bilingual;
            ckBilingual.CheckedChanged += delegate
            {
                try
                {
                    Engine.ToggleBilingual(ckBilingual.Checked);
                    RefreshInfo("双语已" + (ckBilingual.Checked ? "开启" : "关闭") + "（FCC 学习进度自动迁移）");
                }
                catch (Exception ex) { MessageBox.Show("切换失败：" + ex.Message); }
            };
            Controls.Add(ckBilingual);

            ckRemember = new CheckBox();
            ckRemember.Text = "退出后把登录信息与进度保存到 personal.dat 并清除工作区隐私";
            ckRemember.Location = new System.Drawing.Point(20, 148);
            ckRemember.AutoSize = true;
            ckRemember.Checked = true;
            Controls.Add(ckRemember);

            btnImport = MkBtn("导入题库…", 20, 180, 150, 34);
            btnImport.Click += delegate { DoImport(); };

            btnStart = MkBtn("启动模拟考试", 300, 178, 250, 46);
            btnStart.Click += delegate { DoStart(); };

            labBank = new Label(); labBank.Location = new System.Drawing.Point(20, 232); labBank.AutoSize = true; Controls.Add(labBank);
            labPrivacy = new Label(); labPrivacy.Location = new System.Drawing.Point(20, 252); labPrivacy.AutoSize = true; Controls.Add(labPrivacy);
            labStatus = new Label(); labStatus.Location = new System.Drawing.Point(20, 277); labStatus.AutoSize = true;
            labStatus.ForeColor = System.Drawing.Color.Green; Controls.Add(labStatus);

            Load += delegate
            {
                SetBusy(true);
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    string err = null;
                    try { Engine.Prepare(); Engine.RestorePrivacy(); }
                    catch (Exception ex) { err = ex.Message; }
                    BeginInvoke((MethodInvoker)delegate
                    {
                        SetBusy(false);
                        if (err != null) MessageBox.Show("init failed: " + err);
                        else RefreshInfo("就绪。");
                    });
                });
            };
        }

        Button MkBtn(string t, int x, int y, int w, int h)
        {
            Button b = new Button();
            b.Text = t; b.Location = new System.Drawing.Point(x, y); b.Size = new System.Drawing.Size(w, h);
            Controls.Add(b);
            return b;
        }

        void Pick(string bank)
        {
            try
            {
                Engine.SwitchBank(bank);
                imported = null;
                RefreshInfo("已切换到 " + bank + " 题库。");
            }
            catch (Exception ex) { MessageBox.Show("切换失败：" + ex.Message); }
        }

        void DoImport()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Filter = "SQLite 题库|*.sqlite3";
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        string name = Engine.ImportBank(dlg.FileName);
                        imported = name;
                        RefreshInfo("已导入题库 " + name + "（启动时使用）");
                    }
                    catch (Exception ex) { MessageBox.Show("导入失败：" + ex.Message); }
                }
            }
        }

        void DoStart()
        {
            try
            {
                if (imported != null) { Engine.SwitchBank(imported); imported = null; }
                labStatus.Text = "正在启动模拟考试系统…";
                Application.DoEvents();
                Process p = Engine.LaunchApp();
                Enabled = false;
                p.WaitForExit();
                Enabled = true;
                if (ckRemember.Checked)
                {
                    Engine.SavePrivacy();
                    RefreshInfo("已退出。隐私数据已保存到 personal.dat，工作区已清理。");
                }
                else RefreshInfo("已退出（未保存隐私数据）。");
            }
            catch (Exception ex) { Enabled = true; MessageBox.Show("启动失败：" + ex.Message); }
        }

        void SetBusy(bool busy)
        {
            btnCRAC.Enabled = btnFAA.Enabled = btnImport.Enabled = btnStart.Enabled = ckBilingual.Enabled = !busy;
            if (busy) labStatus.Text = "正在准备文件, 请稍候...";
            Application.DoEvents();
        }

        void RefreshInfo(string status)
        {
            labBank.Text = "当前题库：" + Engine.ActiveBank + (Engine.Bilingual ? "（双语）" : "") + "（A/B/C；FCC 模式 A=T级 B=G级 C=E级）";
            labPrivacy.Text = Engine.HasDataFile ? "检测到 personal.dat（启动时自动恢复上次进度）" : "未发现 personal.dat";
            labStatus.Text = status;
        }
    }
}
