// SimPatches —— Simulation.exe 外科手术补丁库
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Simulation;
using WPFClient.App;
using WPFClient.App.Views;

namespace SimPatches
{
    public static class PatchCore
    {
        static readonly Dictionary<int, string> _correct = new Dictionary<int, string>();
        static bool _loaded;

        public static string GetCorrect(int questionId)
        {
            lock (_correct)
            {
                if (!_loaded)
                {
                    try
                    {
                        var dt = SQLiteHelper.GetInstance().GetDataTable("SELECT ID, CorrectOptions FROM Question WHERE CorrectOptions IS NOT NULL AND CorrectOptions != ''");
                        if (dt != null)
                            foreach (System.Data.DataRow r in dt.Rows)
                                _correct[Convert.ToInt32(r["ID"])] = Convert.ToString(r["CorrectOptions"]);
                    }
                    catch { }
                    _loaded = true;
                }
                string v;
                return _correct.TryGetValue(questionId, out v) ? v : null;
            }
        }

        public static bool Judge(object qo)
        {
            var q = qo as QuestionModel;
            if (q == null || string.IsNullOrEmpty(q.UserChoice)) return false;
            var co = GetCorrect(q.QuestionID);
            if (string.IsNullOrEmpty(co))
            {
                int idx = (q.OptionSequence ?? "0123").IndexOf('0');
                return q.UserChoice == idx.ToString();
            }
            var want = co.Split(',').Select(int.Parse).OrderBy(x => x).ToList();
            var seq = q.OptionSequence ?? "0123";
            var sel = q.UserChoice.Where(Char.IsDigit).Select(c => seq[c - '0'] - '0').OrderBy(x => x).ToList();
            return sel.Count == want.Count && !sel.Where((v, i) => v != want[i]).Any();
        }

        public static string CorrectLetters(QuestionModel q)
        {
            var co = GetCorrect(q.QuestionID);
            if (string.IsNullOrEmpty(co)) return null;
            var seq = q.OptionSequence ?? "0123";
            var sb = new StringBuilder();
            foreach (var p in Enumerable.Range(0, 4).Where(p => seq[p] >= '0' && seq[p] <= '3' && co.Contains(seq[p].ToString())))
                sb.Append((char)('A' + p));
            return sb.Length >= 1 ? sb.ToString() : null;
        }

        public static bool IsMulti(QuestionModel q)
        {
            var co = GetCorrect(q.QuestionID);
            return !string.IsNullOrEmpty(co) && co.Contains(',');
        }
    }

    public static class IdCheck
    {
        static readonly string Shape = @"^[1-9]\d{5}(18|19|20)\d{2}((0[1-9])|(10|11|12))(([0-2][1-9])|10|20|30|31)\d{3}[0-9Xx]$";
        static readonly int[] W = { 7, 9, 10, 5, 8, 4, 2, 1, 6, 3, 7, 9, 10, 5, 8, 4, 2 };
        static readonly string Code = "10X98765432";

        public static ValidationResult Validate(string value, CultureInfo culture)
        {
            string s = (value ?? "").Trim();
            bool invalid = string.IsNullOrEmpty(s);
            if (!invalid)
            {
                bool cnLike = s.Length >= 15 && s.Length <= 18 && s.All(c => char.IsDigit(c) || c == 'X' || c == 'x');
                if (cnLike)
                {
                    if (s.Length == 15 && s.All(char.IsDigit)) { }
                    else if (System.Text.RegularExpressions.Regex.IsMatch(s, Shape))
                    {
                        int sum = 0;
                        for (int i = 0; i < 17; i++) sum += (s[i] - '0') * W[i];
                        if (Code[sum % 11] != char.ToUpper(s[17])) invalid = true;
                    }
                    else invalid = true;
                }
            }
            if (invalid)
            {
                Common.UserInfo.IdCardNo = string.Empty;
                return new ValidationResult(false, "请输入正确的身份证号码");
            }
            return new ValidationResult(true, null);
        }
    }

    public static class MultiUI
    {
        public static void HookShowSimu(object pageO) { var page = (Simu)pageO; HookShow(page, RbsSimu(page), (Action)(() => AfterRenderSimu(page)), delegate { return CurrentSimu(page); }); }
        public static void HookShowLearn(object pageO) { var page = (Learn)pageO; HookShow(page, RbsLearn(page), (Action)(() => AfterRenderLearn(page)), delegate { return CurrentLearn(); }); }

        static RadioButton[] RbsSimu(Simu p) { return new[] { p.rbOption1, p.rbOption2, p.rbOption3, p.rbOption4 }; }
        static RadioButton[] RbsLearn(Learn p) { return new[] { p.rbOption1, p.rbOption2, p.rbOption3, p.rbOption4 }; }

        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<RadioButton, object> _wired =
            new System.Runtime.CompilerServices.ConditionalWeakTable<RadioButton, object>();

        static QuestionModel CurrentLearn()
        {
            return Common.listQuestion != null && Common.SelectIndex >= 0 && Common.SelectIndex < Common.listQuestion.Count
                ? Common.listQuestion[Common.SelectIndex] : null;
        }

        static void HookShow(FrameworkElement page, RadioButton[] rbs, Action after, Func<QuestionModel> getQ)
        {
            var curQ = getQ();
            bool multi = PatchCore.IsMulti(curQ);
            foreach (var rb in rbs)
            {
                if (rb == null) continue;
                if (multi)
                {
                    // 多选题：独立分组（可同时勾选多个）
                    if (rb.GroupName == null || rb.GroupName == "" || rb.GroupName.StartsWith("_ms"))
                        rb.GroupName = "_ms" + Guid.NewGuid().ToString("N");
                }
                else
                {
                    // 单选题：恢复正常互斥（清空自定义分组）
                    if (rb.GroupName != null && rb.GroupName.StartsWith("_ms"))
                        rb.GroupName = "";
                }
                object dummy;
                if (!_wired.TryGetValue(rb, out dummy))
                {
                    System.Windows.Input.MouseButtonEventHandler h = delegate(object s, System.Windows.Input.MouseButtonEventArgs e)
                    {
                        var b = s as RadioButton;
                        if (b == null || b.IsChecked != true) return;
                        var q = getQ();
                        if (!PatchCore.IsMulti(q)) return; // 单选题不允许取消
                        b.IsChecked = false;
                        e.Handled = true;
                        if (q != null) q.UserChoice = JoinedChoice(rbs);
                    };
                    rb.PreviewMouseLeftButtonDown += h;
                    _wired.Add(rb, h);
                }
            }
            page.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, after);
        }

        static void AfterRenderSimu(Simu page)
        {
            var q = CurrentSimu(page);
            if (q == null) return;
            RestoreChecks(RbsSimu(page), q);
            FixAnswerText(page.txtAnswer, q);
            Labels.FixKindText(FindTextBlock(page, "txtKind"));
            UpdateExamProgress(page);
        }

        static void UpdateExamProgress(Simu page)
        {
            try
            {
                var win = page.ParentWin;
                if (win == null) return;
                int cur = page.lvAnwser != null ? page.lvAnwser.SelectedIndex + 1 : 0;
                int total = Common.listQuestion != null ? Common.listQuestion.Count : 0;
                int answered = 0;
                if (Common.listQuestion != null)
                    foreach (var x in Common.listQuestion) if (!string.IsNullOrEmpty(x.UserChoice)) answered++;
                win.Title = string.Format("第{0}题/共{1}题 · 已答{2}题 · BI2XM魔改版", cur, total, answered);
            }
            catch { }
        }

        static void AfterRenderLearn(Learn page)
        {
            var q = Common.listQuestion != null && Common.SelectIndex >= 0 && Common.SelectIndex < Common.listQuestion.Count
                ? Common.listQuestion[Common.SelectIndex] : null;
            if (q == null) return;
            RestoreChecks(RbsLearn(page), q);
            FixAnswerText(page.txtAnswer, q);
            try
            {
                var win = page.ParentWin;
                if (win == null) return;
                int cur = Common.SelectIndex + 1;
                int total = Common.listQuestion.Count;
                win.Title = string.Format("学习中 · 第{0}题/共{1}题 · BI2XM魔改版", cur, total);
            }
            catch { }
        }

        static QuestionModel CurrentSimu(Simu page)
        {
            return Common.listQuestion != null && page.lvAnwser != null && page.lvAnwser.SelectedIndex >= 0
                && page.lvAnwser.SelectedIndex < Common.listQuestion.Count
                ? Common.listQuestion[page.lvAnwser.SelectedIndex] : null;
        }

        static void RestoreChecks(RadioButton[] rbs, QuestionModel q)
        {
            var uc = q.UserChoice ?? "";
            for (int i = 0; i < 4 && i < rbs.Length; i++)
            {
                if (rbs[i] == null) continue;
                bool want = uc.Contains(i.ToString(CultureInfo.InvariantCulture));
                if ((rbs[i].IsChecked == true) != want) rbs[i].IsChecked = want;
            }
        }

        static void FixAnswerText(TextBlock txtAnswer, QuestionModel q)
        {
            if (txtAnswer == null) return;
            var letters = PatchCore.CorrectLetters(q);
            if (letters != null) txtAnswer.Text = letters;
        }

        static TextBlock FindTextBlock(DependencyObject root, string name)
        {
            if (root == null) return null;
            var fe = root as FrameworkElement;
            if (fe != null && fe.Name == name && fe is TextBlock) return (TextBlock)fe;
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var r = FindTextBlock(VisualTreeHelper.GetChild(root, i), name);
                if (r != null) return r;
            }
            return null;
        }

        public static void OnCheckedSimu(object pageO, RoutedEventArgs e)
        {
            var page = (Simu)pageO;
            if (page.spCheckAnswer.Visibility == Visibility.Collapsed && page.spAnswer.Visibility == Visibility.Collapsed)
                page.spCheckAnswer.Visibility = Visibility.Visible;
            var q = CurrentSimu(page);
            if (q != null) q.UserChoice = JoinedChoice(RbsSimu(page));
            int idx = page.lvAnwser.SelectedIndex;
            if (idx >= 0 && idx < page.listAnswerSheet.Count)
            {
                page.listAnswerSheet[idx].ellipse.Fill = Common.ConvertColor("#FF3C9DFE");
                page.listAnswerSheet[idx].txtQuestionNo.Foreground = Brushes.White;
            }
        }

        public static void OnCheckedLearn(object pageO, RoutedEventArgs e)
        {
            var page = (Learn)pageO;
            if (page.spCheckAnswer.Visibility == Visibility.Collapsed && page.spAnswer.Visibility == Visibility.Collapsed)
                page.spCheckAnswer.Visibility = Visibility.Visible;
            var q = Common.listQuestion != null && Common.SelectIndex >= 0 && Common.SelectIndex < Common.listQuestion.Count
                ? Common.listQuestion[Common.SelectIndex] : null;
            if (q != null) q.UserChoice = JoinedChoice(RbsLearn(page));
        }

        static string JoinedChoice(RadioButton[] rbs)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < rbs.Length; i++)
                if (rbs[i] != null && rbs[i].IsChecked == true) sb.Append(i);
            return sb.ToString();
        }
    }

    public static class KindPatch
    {
        public static void ConvertAndAlias(System.Data.DataRow row)
        {
            var m = new KindModel
            {
                ID = Convert.ToInt32(row["ID"]),
                KindName = Convert.ToString(row["KindName"]),
                Version = Convert.ToString(row["Version"]),
                QuestionAmount = Convert.ToInt32(row["QuestionAmount"]),
                CutOffScores = Convert.ToInt32(row["CutOffScores"]),
                ExamDuration = Convert.ToInt32(row["ExamDuration"])
            };
            Common.dicKindInfo[m.KindName] = m;
            string alias;
            if (m.KindName == "T") alias = "A";
            else if (m.KindName == "G") alias = "B";
            else if (m.KindName == "E") alias = "C";
            else alias = null;
            if (alias != null) Common.dicKindInfo[alias] = m;
        }
    }

    public static class ReviewFix
    {
        static System.Collections.Generic.HashSet<int> CorrectPositions(QuestionModel q)
        {
            var set = new System.Collections.Generic.HashSet<int>();
            var co = PatchCore.GetCorrect(q.QuestionID);
            if (string.IsNullOrEmpty(co)) { var seq0 = q.OptionSequence ?? "0123"; set.Add(seq0.IndexOf('0')); return set; }
            var seq = q.OptionSequence ?? "0123";
            for (int p = 0; p < seq.Length && p < 4; p++)
                if (seq[p] >= '0' && seq[p] <= '3' && co.Contains(seq[p].ToString())) set.Add(p);
            return set;
        }

        static System.Collections.Generic.HashSet<int> ChosenPositions(QuestionModel q)
        {
            var set = new System.Collections.Generic.HashSet<int>();
            foreach (char c in (q.UserChoice ?? "")) if (c >= '0' && c <= '3') set.Add(c - '0');
            return set;
        }

        public static string ChoiceLetters(string uc)
        {
            var sb = new StringBuilder();
            foreach (char c in (uc ?? "")) if (c >= '0' && c <= '3') sb.Append((char)('A' + (c - '0')));
            var s = sb.ToString();
            return s.Length > 0 ? s : "未选择";
        }

        public static void FixMistakeView(object pageO)
        {
            var page = pageO as Mistake;
            if (page == null) return;
            page.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (Action)(() =>
            {
                try
                {
                    var q = (Common.listQuestion != null && page.lvAnwser != null && page.lvAnwser.SelectedIndex >= 0
                        && page.lvAnwser.SelectedIndex < Common.listQuestion.Count)
                        ? Common.listQuestion[page.lvAnwser.SelectedIndex] : null;
                    if (q == null || !PatchCore.IsMulti(q)) return;
                    page.txtMyAnswer.Text = ChoiceLetters(q.UserChoice);
                    var letters = PatchCore.CorrectLetters(q);
                    page.txtRightAnswer.Text = letters ?? "";
                }
                catch { }
            }));
        }

        public static void FixAnalysisList(object pageO)
        {
            var page = pageO as SimuFinished;
            if (page == null) return;
            page.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (Action)(() =>
            {
                try
                {
                var cols = new System.Collections.Generic.List<System.Collections.IEnumerable>();
                cols.Add(page.listQAll); cols.Add(page.listQRight); cols.Add(page.listQWrong);
                foreach (var col in cols)
                {
                    if (col == null) continue;
                    foreach (var o in col)
                    {
                        var u = o as ucQuestion;
                        if (u == null) continue;
                        QuestionModel q = null;
                        if (Common.listQuestion != null)
                            foreach (var x in Common.listQuestion) { if (x.QuestionID == u.QuestionID) { q = x; break; } }
                        if (q == null || !PatchCore.IsMulti(q)) continue;
                        var correct = CorrectPositions(q);
                        var chosen = ChosenPositions(q);
                        var rbs = new[] { u.rbOption1, u.rbOption2, u.rbOption3, u.rbOption4 };
                        for (int i = 0; i < rbs.Length; i++)
                        {
                            if (rbs[i] == null) continue;
                            bool want = chosen.Contains(i);
                            if ((rbs[i].IsChecked == true) != want) rbs[i].IsChecked = want;
                        }
                        if (u.txtMyAnswer != null) u.txtMyAnswer.Text = ChoiceLetters(q.UserChoice);
                        if (u.txtRightAnswer != null) u.txtRightAnswer.Text = PatchCore.CorrectLetters(q) ?? "";
                    }
                }
                }
                catch { }
            }));
        }
    }

    public static class Branding
    {
        const string Suffix = " · BI2XM 魔改版 仅供学习使用 严禁售卖";

        public static void OnNavigate(object winO)
        {
            try
            {
                var win = winO as MainWindow;
                if (win == null) return;
                // 强制收起加载遮罩（灰带）：URL 为空后 StopLoading 可能不被调用
                if (win.grdGrayLayer != null && win.grdGrayLayer.Visibility != Visibility.Collapsed)
                    win.grdGrayLayer.Visibility = Visibility.Collapsed;
                if (win.bdLoading != null && win.bdLoading.Visibility != Visibility.Collapsed)
                    win.bdLoading.Visibility = Visibility.Collapsed;
                if (win.title != null && win.title.Inlines.Count == 0
                    && !string.IsNullOrEmpty(win.title.Text) && !win.title.Text.Contains("魔改版"))
                {
                    string orig = win.title.Text;
                    double baseSize = win.title.FontSize > 0 ? win.title.FontSize : 20;
                    win.title.Text = null;
                    win.title.Inlines.Add(new System.Windows.Documents.Run(orig) { FontSize = baseSize });
                    win.title.Inlines.Add(new System.Windows.Documents.Run(Suffix)
                    {
                        FontSize = Math.Max(12, baseSize - 6),
                        Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99))
                    });
                    win.title.TextWrapping = TextWrapping.NoWrap;
                }
                if (!string.IsNullOrEmpty(win.Title) && !win.Title.Contains("魔改版"))
                    win.Title = win.Title + Suffix;
            }
            catch { }
        }
    }

    public static class Labels
    {
        public static void FixHome(object pageO)
        {
            var page = (Home)pageO;
            page.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, (Action)(() =>
            {
                // 主页窗口标题显示学习统计
                try
                {
                    var win = page.ParentWin;
                    if (win != null)
                    {
                        var secs = Simulation.SQLite.QueryLearnTime();
                        int totalSec;
                        int.TryParse(secs ?? "0", out totalSec);
                        int hours = totalSec / 3600, mins = (totalSec % 3600) / 60;
                        string timeStr = hours > 0 ? string.Format("{0}小时{1}分", hours, mins) : string.Format("{2}分钟", mins);
                        win.Title = string.Format("累计学习 {0} · BI2XM魔改版·仅供学习·严禁售卖", timeStr);
                    }
                }
                catch { }
                var dic = Common.dicKindInfo;
                bool faa = dic != null && dic.ContainsKey("T");
                if (!faa) return;
                var map = new Dictionary<string, string>
                {
                    { "业余电台操作技术能力验证-A类", "业余电台操作技术能力验证-T级（Technician）" },
                    { "业余电台操作技术能力验证-B类", "业余电台操作技术能力验证-G级（General）" },
                    { "业余电台操作技术能力验证-C类", "业余电台操作技术能力验证-E级（Amateur Extra）" }
                };
                Walk(page, el =>
                {
                    var tb = el as TextBlock;
                    if (tb == null || tb.Text == null) return;
                    string t;
                    if (map.TryGetValue(tb.Text.Trim(), out t)) tb.Text = t;
                });
            }));
        }

        public static void FixKindText(TextBlock tb)
        {
            if (tb == null) return;
            var dic = Common.dicKindInfo;
            bool faa = dic != null && dic.ContainsKey("T");
            if (!faa) return;
            var map = new Dictionary<string, string> {
                { "A类", "T级 Technician" }, { "B类", "G级 General" }, { "C类", "E级 Extra" } };
            string t;
            if (tb.Text != null && map.TryGetValue(tb.Text.Trim(), out t)) tb.Text = t;
        }

        static void Walk(DependencyObject root, Action<DependencyObject> visit)
        {
            if (root == null) return;
            visit(root);
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++) Walk(VisualTreeHelper.GetChild(root, i), visit);
        }
    }
}
