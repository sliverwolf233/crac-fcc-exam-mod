// ============================================================================
//  BankSwitcher.cs —— “题库切换 - CRAC模拟考试系统”切换器
//
//  目标平台：.NET Framework 4.x（Win7 及以上），WinForms；
//  使用 Windows 系统自带 csc.exe 编译（C# 5 语法，不用字符串插值等新特性）：
//    %WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
//      /target:winexe /codepage:65001 /optimize+
//      /r:System.Windows.Forms.dll /r:System.Drawing.dll
//      /out:BankSwitcher.exe BankSwitcher.cs
//  （编译细节见同目录 build.bat）
//
//  设计原则：不修改 Simulation.exe 的任何一个字节。本程序只管理以下文件：
//    安装目录\SimulationSys.sqlite3          当前活动题库（模拟考试软件直接读取）
//    安装目录\banks\CRAC.sqlite3             中国大陆 CRAC 2025 题库银行
//    安装目录\banks\FAA.sqlite3              美国 FCC 题库银行
//    安装目录\banks\active.txt               当前活动题库标记（内容：CRAC 或 FAA）
//    安装目录\banks\original-backup.sqlite3  首次初始化前对原活动库的一次性备份
//
//  “切换”动作 = 先把当前活动库回存到它所属的银行（保留该题库下的用户学习/
//  考试记录），再把目标银行复制为活动库，最后更新 active.txt。
//
//  命令行参数：
//    （无参数）              启动图形界面
//    --init                  无头首次初始化（由 install-first-run.bat 调用），
//                            结果写入 exe 旁 BankSwitcher-init.log
//    --selftest [日志路径]   无头自检：在 %TEMP% 沙箱中验证全部切换逻辑，
//                            不弹任何窗口；退出码 0=全部通过，1=有失败项
// ============================================================================

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace BankSwitcher
{
    /// <summary>单个题库（银行）的描述信息。</summary>
    internal sealed class BankInfo
    {
        public readonly string Key;       // active.txt 中使用的标识：CRAC / FAA
        public readonly string Name;      // 界面显示名称
        public readonly string FileName;  // banks 目录下的文件名
        public readonly string Subtitle;  // 按钮第二行说明

        public BankInfo(string key, string name, string fileName, string subtitle)
        {
            Key = key;
            Name = name;
            FileName = fileName;
            Subtitle = subtitle;
        }
    }

    /// <summary>全部可切换题库。All[0] 为首次运行时的默认题库。</summary>
    internal static class Banks
    {
        public static readonly BankInfo Crac = new BankInfo("CRAC", "中国大陆 CRAC 2025", "CRAC.sqlite3", "（A / B / C 类）");
        public static readonly BankInfo Faa = new BankInfo("FAA", "美国 FCC (FAA)", "FAA.sqlite3", "（T / G / E 级）");
        public static readonly BankInfo[] All = new BankInfo[] { Crac, Faa };

        public static BankInfo Find(string key)
        {
            if (key == null)
            {
                return null;
            }
            for (int i = 0; i < All.Length; i++)
            {
                if (string.Equals(All[i].Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return All[i];
                }
            }
            return null;
        }
    }

    /// <summary>
    /// 切换引擎：全部文件操作集中在这里；图形界面与自检共用同一套逻辑。
    /// 约定：文件被占用等 IO 失败直接抛 IOException / UnauthorizedAccessException，
    /// 由调用方负责以中文提示用户。
    /// </summary>
    internal sealed class SwitcherEngine
    {
        public const string ActiveDbName = "SimulationSys.sqlite3";
        public const string ActiveMarkName = "active.txt";
        public const string BackupName = "original-backup.sqlite3";

        public readonly string BaseDir;        // 安装目录（本程序所在目录 = Simulation.exe 所在目录）
        public readonly string BanksDir;       // BaseDir\banks
        public readonly string ActiveDbPath;   // BaseDir\SimulationSys.sqlite3
        public readonly string ActiveMarkPath; // BanksDir\active.txt
        public readonly string BackupPath;     // BanksDir\original-backup.sqlite3

        public SwitcherEngine(string baseDir)
        {
            BaseDir = baseDir;
            BanksDir = Path.Combine(baseDir, "banks");
            ActiveDbPath = Path.Combine(baseDir, ActiveDbName);
            ActiveMarkPath = Path.Combine(BanksDir, ActiveMarkName);
            BackupPath = Path.Combine(BanksDir, BackupName);
        }

        public string GetBankPath(BankInfo bank)
        {
            return Path.Combine(BanksDir, bank.FileName);
        }

        public bool BankFileExists(BankInfo bank)
        {
            return File.Exists(GetBankPath(bank));
        }

        /// <summary>读取当前活动题库标记；文件缺失或内容非法时返回 null。</summary>
        public string ReadActiveKey()
        {
            try
            {
                if (!File.Exists(ActiveMarkPath))
                {
                    return null;
                }
                string s = File.ReadAllText(ActiveMarkPath, Encoding.UTF8).Trim();
                BankInfo b = Banks.Find(s);
                return (b == null) ? null : b.Key;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 首次运行初始化：active.txt 缺失或活动库文件缺失时视为首次运行，
        /// 默认激活 CRAC（若 CRAC 银行不存在则选 FAA），直接复制、不回存。
        /// 若安装目录已带有原版 SimulationSys.sqlite3（例如刚装完原软件），
        /// 先做一次性备份到 banks\original-backup.sqlite3，避免原记录被覆盖。
        /// 返回激活后的题库 Key；两个题库文件都不存在时返回 null。
        /// </summary>
        public string EnsureInitialized()
        {
            string key = ReadActiveKey();
            if (key != null && File.Exists(ActiveDbPath))
            {
                return key; // 已初始化，不做任何复制
            }

            // 选择默认题库：优先 CRAC，其次 FAA；都没有则无法初始化
            BankInfo pick = Banks.All[0];
            if (!BankFileExists(pick))
            {
                if (BankFileExists(Banks.All[1]))
                {
                    pick = Banks.All[1];
                }
                else
                {
                    return null;
                }
            }

            // 首次激活前对已存在的活动库做一次性备份（仅第一次）
            if (File.Exists(ActiveDbPath) && !File.Exists(BackupPath))
            {
                File.Copy(ActiveDbPath, BackupPath, false);
            }

            File.Copy(GetBankPath(pick), ActiveDbPath, true);
            WriteActiveKey(pick.Key);
            return pick.Key;
        }

        /// <summary>写入当前活动题库标记（UTF-8 无 BOM，内容为 CRAC 或 FAA）。</summary>
        private void WriteActiveKey(string key)
        {
            if (!Directory.Exists(BanksDir))
            {
                Directory.CreateDirectory(BanksDir);
            }
            File.WriteAllText(ActiveMarkPath, key, new UTF8Encoding(false));
        }

        /// <summary>删除活动库的 SQLite 残留事务文件（-journal / -wal / -shm）。
        /// 只应在模拟考试软件已关闭时执行。</summary>
        public void DeleteActiveDbJournals()
        {
            string[] suffixes = new string[] { "-journal", "-wal", "-shm" };
            for (int i = 0; i < suffixes.Length; i++)
            {
                string p = ActiveDbPath + suffixes[i];
                if (File.Exists(p))
                {
                    File.Delete(p);
                }
            }
        }

        /// <summary>
        /// 切换到目标题库：
        ///   1) 清理活动库残留的 -journal/-wal/-shm；
        ///   2) 若当前活动库存在，先回存到它所属的银行（保留用户数据）；
        ///   3) 把目标银行复制为活动库（File.Cope overwrite:true）；
        ///   4) 更新 active.txt。
        /// 文件被占用等 IO 失败会抛出异常，由调用方提示。
        /// </summary>
        public void SwitchTo(BankInfo target)
        {
            string current = ReadActiveKey();
            DeleteActiveDbJournals();
            if (current != null && File.Exists(ActiveDbPath))
            {
                File.Copy(ActiveDbPath, GetBankPath(Banks.Find(current)), true);
            }
            File.Copy(GetBankPath(target), ActiveDbPath, true);
            WriteActiveKey(target.Key);
        }

        /// <summary>启动同目录下的 Simulation.exe（工作目录设为安装目录，
        /// 与原软件的相对路径连接串 Data Source=SimulationSys.sqlite3 匹配）。</summary>
        public void LaunchSimulation()
        {
            string exe = Path.Combine(BaseDir, "Simulation.exe");
            if (!File.Exists(exe))
            {
                throw new FileNotFoundException("未找到 Simulation.exe，请确认本程序位于模拟考试软件的安装目录。", exe);
            }
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = exe;
            psi.WorkingDirectory = BaseDir;
            Process.Start(psi);
        }
    }

    /// <summary>主窗体：两个大题库按钮 + 当前题库显示 + “切换后启动”复选框。</summary>
    internal sealed class MainForm : Form
    {
        private readonly SwitcherEngine engine;
        private Button btnCrac;
        private Button btnFaa;
        private Label lblCurrent;
        private CheckBox chkLaunch;

        public MainForm()
            : this(AppDomain.CurrentDomain.BaseDirectory)
        {
        }

        public MainForm(string baseDir)
        {
            engine = new SwitcherEngine(baseDir);
            BuildUi();
        }

        private void BuildUi()
        {
            Text = "题库切换 - CRAC模拟考试系统";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Font;
            Font = new Font("Microsoft YaHei", 9F);
            ClientSize = new Size(470, 270);

            btnCrac = MakeBankButton(Banks.Crac, 18, 16);
            btnFaa = MakeBankButton(Banks.Faa, 242, 16);

            lblCurrent = new Label();
            lblCurrent.SetBounds(18, 122, 434, 28);
            lblCurrent.TextAlign = ContentAlignment.MiddleCenter;
            lblCurrent.Font = new Font("Microsoft YaHei", 11F, FontStyle.Bold);
            lblCurrent.Text = "当前题库：";

            chkLaunch = new CheckBox();
            chkLaunch.SetBounds(20, 158, 430, 24);
            chkLaunch.Text = "切换后启动模拟考试软件";
            chkLaunch.Checked = true;

            Label lblHint = new Label();
            lblHint.SetBounds(18, 196, 434, 60);
            lblHint.ForeColor = SystemColors.GrayText;
            lblHint.Text = "提示：切换题库前请先完全关闭模拟考试软件。\r\n各题库独立保存学习与考试记录，切换不会丢失数据。";

            Controls.Add(btnCrac);
            Controls.Add(btnFaa);
            Controls.Add(lblCurrent);
            Controls.Add(chkLaunch);
            Controls.Add(lblHint);
        }

        private Button MakeBankButton(BankInfo bank, int x, int y)
        {
            Button b = new Button();
            b.SetBounds(x, y, 210, 96);
            b.FlatStyle = FlatStyle.Standard;
            b.UseVisualStyleBackColor = false;
            b.Font = new Font("Microsoft YaHei", 11F, FontStyle.Bold);
            b.Text = bank.Name + "\r\n" + bank.Subtitle;
            b.Tag = bank;
            b.Click += new EventHandler(OnBankButtonClick);
            return b;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            InitOnce();
        }

        /// <summary>启动时执行一次首次运行初始化（若需要），然后刷新状态显示。</summary>
        private void InitOnce()
        {
            try
            {
                string key = engine.EnsureInitialized();
                if (key == null)
                {
                    MessageBox.Show(this,
                        "未找到题库文件：\r\n  banks\\CRAC.sqlite3\r\n  banks\\FAA.sqlite3\r\n\r\n请先运行 install-first-run.bat 完成安装初始化。",
                        "题库切换", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                ShowIoError(ex, "初始化");
            }
            UpdateStatus();
        }

        private void OnBankButtonClick(object sender, EventArgs e)
        {
            Button btn = (Button)sender;
            BankInfo bank = (BankInfo)btn.Tag;

            // 目标银行文件必须存在
            if (!engine.BankFileExists(bank))
            {
                MessageBox.Show(this,
                    "题库文件缺失：\r\n  banks\\" + bank.FileName + "\r\n\r\n请重新运行 install-first-run.bat 复制题库文件。",
                    "题库切换", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string current = engine.ReadActiveKey();

            // 已是当前题库：提示（勾选了自动启动时直接启动）
            if (current == bank.Key)
            {
                if (chkLaunch.Checked)
                {
                    TryLaunch();
                }
                else
                {
                    MessageBox.Show(this, "已是当前题库：" + bank.Name, "题库切换",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return;
            }

            // 切换前确认：提醒关闭模拟考试软件
            DialogResult r = MessageBox.Show(this,
                "切换前请确保“模拟考试软件”已完全关闭。\r\n\r\n是否切换到：" + bank.Name + "？",
                "确认切换", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes)
            {
                return;
            }

            try
            {
                engine.SwitchTo(bank);
            }
            catch (IOException ex)
            {
                ShowIoError(ex, "切换");
                return;
            }
            catch (UnauthorizedAccessException ex)
            {
                ShowIoError(ex, "切换");
                return;
            }

            UpdateStatus();

            if (chkLaunch.Checked)
            {
                TryLaunch();
            }
            else
            {
                MessageBox.Show(this,
                    "已切换到：" + bank.Name + "\r\n\r\n该题库的学习与考试记录已保存在其银行文件中。",
                    "题库切换", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void TryLaunch()
        {
            try
            {
                engine.LaunchSimulation();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "启动模拟考试软件失败：" + ex.Message, "题库切换",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ShowIoError(Exception ex, string action)
        {
            string msg = string.Format(
                "{0}失败：{1}\r\n\r\n常见原因：文件被占用或没有写权限。\r\n" +
                "请先完全关闭模拟考试软件后重试；若仍失败，请右键选择“以管理员身份运行”。",
                action, ex.Message);
            MessageBox.Show(this, msg, "题库切换", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        /// <summary>刷新“当前题库”文字与按钮底色（浅绿 = 当前题库）。</summary>
        private void UpdateStatus()
        {
            BankInfo cur = Banks.Find(engine.ReadActiveKey());
            lblCurrent.Text = (cur == null) ? "当前题库：未初始化" : "当前题库：" + cur.Name;
            SetButtonState(btnCrac, Banks.Crac, cur);
            SetButtonState(btnFaa, Banks.Faa, cur);
        }

        private void SetButtonState(Button b, BankInfo bank, BankInfo cur)
        {
            bool active = (bank == cur);
            b.BackColor = active ? Color.FromArgb(0xE2, 0xEF, 0xDA) : SystemColors.Control;
        }
    }

    /// <summary>--init：无头首次初始化，供 install-first-run.bat 调用。</summary>
    internal static class InitRunner
    {
        public static int Run()
        {
            string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BankSwitcher-init.log");
            int code = 0;
            try
            {
                SwitcherEngine eng = new SwitcherEngine(AppDomain.CurrentDomain.BaseDirectory);
                string key = eng.EnsureInitialized();
                using (StreamWriter w = new StreamWriter(logPath, false, new UTF8Encoding(false)))
                {
                    if (key == null)
                    {
                        w.WriteLine("[失败] 未找到题库文件 banks\\CRAC.sqlite3 / banks\\FAA.sqlite3，无法完成首次激活。");
                        code = 2;
                    }
                    else
                    {
                        w.WriteLine("[成功] 初始化完成，当前题库：" + Banks.Find(key).Name);
                        code = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                try
                {
                    File.WriteAllText(logPath,
                        "[失败] 初始化异常：" + ex.Message + "\r\n请先完全关闭模拟考试软件；若目录受保护请以管理员身份运行。",
                        new UTF8Encoding(false));
                }
                catch
                {
                    // 日志写不进去时忽略，退出码已能表明失败
                }
                code = 3;
            }
            return code;
        }
    }

    /// <summary>--selftest：在 %TEMP% 沙箱目录里验证切换引擎的全部行为，不弹窗。</summary>
    internal static class SelfTest
    {
        private static StreamWriter log;
        private static int failed;

        private static void Check(bool ok, string name)
        {
            log.WriteLine((ok ? "[通过] " : "[失败] ") + name);
            if (!ok)
            {
                failed++;
            }
        }

        private static string ReadText(string path)
        {
            return File.ReadAllText(path);
        }

        public static int Run(string logPath)
        {
            failed = 0;
            log = new StreamWriter(logPath, false, new UTF8Encoding(false));
            try
            {
                RunCore();
                log.WriteLine();
                log.WriteLine(failed == 0 ? "结论：全部自检通过。" : string.Format("结论：{0} 项自检失败。", failed));
            }
            finally
            {
                log.Close();
            }
            return failed == 0 ? 0 : 1;
        }

        private static void RunCore()
        {
            log.WriteLine("BankSwitcher 自检开始：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            string root = Path.Combine(Path.GetTempPath(), "BankSwitcherSelfTest");
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
            Directory.CreateDirectory(root);

            // ---------- 场景一：全新安装（无 active.txt、无活动库） ----------
            string dir1 = Path.Combine(root, "case1");
            string banks1 = Path.Combine(dir1, "banks");
            Directory.CreateDirectory(banks1);
            File.WriteAllText(Path.Combine(banks1, "CRAC.sqlite3"), "CRAC-BANK-V1");
            File.WriteAllText(Path.Combine(banks1, "FAA.sqlite3"), "FAA-BANK-V1");
            SwitcherEngine e1 = new SwitcherEngine(dir1);

            string k = e1.EnsureInitialized();
            Check(k == "CRAC", "场景1 首次运行默认激活 CRAC");
            Check(ReadText(e1.ActiveDbPath) == "CRAC-BANK-V1", "场景1 活动库内容 = CRAC 银行");
            Check(ReadText(e1.ActiveMarkPath).Trim() == "CRAC", "场景1 active.txt = CRAC");

            // 幂等：已初始化时再次调用不应重新复制
            File.WriteAllText(e1.ActiveDbPath, "CRAC-USER-V9");
            k = e1.EnsureInitialized();
            Check(k == "CRAC" && ReadText(e1.ActiveDbPath) == "CRAC-USER-V9", "场景1 重复初始化不覆盖活动库");

            // ---------- 场景二：原软件已带活动库（应先备份再激活） ----------
            string dir2 = Path.Combine(root, "case2");
            string banks2 = Path.Combine(dir2, "banks");
            Directory.CreateDirectory(banks2);
            File.WriteAllText(Path.Combine(dir2, "SimulationSys.sqlite3"), "ORIGINAL-USERDATA");
            File.WriteAllText(Path.Combine(banks2, "CRAC.sqlite3"), "CRAC2-BANK");
            SwitcherEngine e2 = new SwitcherEngine(dir2);

            k = e2.EnsureInitialized();
            Check(k == "CRAC" && ReadText(e2.ActiveDbPath) == "CRAC2-BANK", "场景2 首次激活覆盖为 CRAC 银行");
            Check(ReadText(e2.BackupPath) == "ORIGINAL-USERDATA", "场景2 原活动库已一次性备份");

            File.WriteAllText(e2.ActiveDbPath, "NEW-STUFF");
            File.Delete(e2.ActiveMarkPath);
            e2.EnsureInitialized();
            Check(ReadText(e2.BackupPath) == "ORIGINAL-USERDATA", "场景2 备份仅保留首份（不二次覆盖）");

            // ---------- 场景三：完整切换 + 回存 + journal 清理 ----------
            string dir3 = Path.Combine(root, "case3");
            string banks3 = Path.Combine(dir3, "banks");
            Directory.CreateDirectory(banks3);
            string cracFile3 = Path.Combine(banks3, "CRAC.sqlite3");
            string faaFile3 = Path.Combine(banks3, "FAA.sqlite3");
            File.WriteAllText(cracFile3, "C0");
            File.WriteAllText(faaFile3, "F0");
            SwitcherEngine e3 = new SwitcherEngine(dir3);

            e3.EnsureInitialized();
            File.WriteAllText(e3.ActiveDbPath, "C1-USER");
            File.WriteAllText(e3.ActiveDbPath + "-journal", "junk");
            File.WriteAllText(e3.ActiveDbPath + "-wal", "junk");
            File.WriteAllText(e3.ActiveDbPath + "-shm", "junk");

            e3.SwitchTo(Banks.Faa);
            Check(ReadText(e3.ActiveDbPath) == "F0", "场景3 切换后活动库 = FAA 银行");
            Check(ReadText(cracFile3) == "C1-USER", "场景3 用户数据已回存到 CRAC 银行");
            Check(ReadText(e3.ActiveMarkPath).Trim() == "FAA", "场景3 active.txt = FAA");
            Check(!File.Exists(e3.ActiveDbPath + "-journal")
               && !File.Exists(e3.ActiveDbPath + "-wal")
               && !File.Exists(e3.ActiveDbPath + "-shm"), "场景3 残留 -journal/-wal/-shm 已清理");

            File.WriteAllText(e3.ActiveDbPath, "F2-USER");
            e3.SwitchTo(Banks.Crac);
            Check(ReadText(e3.ActiveDbPath) == "C1-USER", "场景3 切回后活动库恢复 CRAC 用户数据");
            Check(ReadText(faaFile3) == "F2-USER", "场景3 FAA 用户数据已回存");

            // ---------- 场景四：活动库被占用 → IOException，且不破坏现场 ----------
            using (FileStream lockFs = new FileStream(e3.ActiveDbPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                try
                {
                    e3.SwitchTo(Banks.Faa);
                    Check(false, "场景4 活动库被占用时应抛 IOException");
                }
                catch (IOException)
                {
                    Check(true, "场景4 活动库被占用时抛 IOException");
                }
            }
            Check(ReadText(e3.ActiveMarkPath).Trim() == "CRAC", "场景4 失败后 active.txt 不变");
            Check(ReadText(faaFile3) == "F2-USER", "场景4 失败后 FAA 银行未被破坏");

            // ---------- 场景五：活动库文件缺失（active.txt 还在）→ 按首次运行恢复 ----------
            File.Delete(e3.ActiveDbPath);
            k = e3.EnsureInitialized();
            Check(k == "CRAC" && ReadText(e3.ActiveDbPath) == "C1-USER", "场景5 活动库缺失时从银行恢复");

            // ---------- 场景六：非法标记 / 无题库 / 仅有 FAA ----------
            File.WriteAllText(e3.ActiveMarkPath, "###GARBAGE###");
            Check(e3.ReadActiveKey() == null, "场景6 非法 active.txt 视为未初始化");

            string dir7 = Path.Combine(root, "case7");
            Directory.CreateDirectory(Path.Combine(dir7, "banks"));
            Check(new SwitcherEngine(dir7).EnsureInitialized() == null, "场景6 无题库文件时返回 null");

            string dir8 = Path.Combine(root, "case8");
            string banks8 = Path.Combine(dir8, "banks");
            Directory.CreateDirectory(banks8);
            File.WriteAllText(Path.Combine(banks8, "FAA.sqlite3"), "F8");
            k = new SwitcherEngine(dir8).EnsureInitialized();
            Check(k == "FAA", "场景6 仅存在 FAA 银行时默认选 FAA");

            Directory.Delete(root, true);
            log.WriteLine("（自检沙箱已清理：%TEMP%\\BankSwitcherSelfTest）");
        }
    }

    /// <summary>程序入口。无参数启动界面；--init / --selftest 为无头模式。</summary>
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args != null && args.Length > 0 && args[0] != null && args[0].StartsWith("--", StringComparison.Ordinal))
            {
                string cmd = args[0].ToLowerInvariant();
                if (cmd == "--selftest")
                {
                    string logPath = (args.Length > 1)
                        ? args[1]
                        : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest.log");
                    return SelfTest.Run(logPath);
                }
                if (cmd == "--init")
                {
                    return InitRunner.Run();
                }
                return 1; // 未知的 -- 选项：不启动界面，直接退出
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }
    }
}
