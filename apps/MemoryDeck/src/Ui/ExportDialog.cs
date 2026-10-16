using System.Text;
using MemoryDeck.Core;

namespace MemoryDeck.Ui;

/// <summary>
/// 「导出给网页版 AI」对话框。
///
/// 目标很具体：产出一段可以直接 Ctrl+V 粘进 ChatGPT / Claude / DeepSeek 等
/// 网页对话框的纯文本，不需要用户再手工删标记、拼格式。
/// 因此这里给的是**纯文本预览**，而不是导出文件后让用户自己打开。
/// </summary>
public sealed class ExportDialog : Form
{
    private readonly MemoryRepository _repo;
    private readonly IReadOnlyList<MemoryEntry> _entries;

    private readonly TextBox _preview = new();
    private readonly CheckBox _chkPreferences = new() { Text = "带上协作偏好（来自 AGENTS.md）", AutoSize = true };
    private readonly CheckBox _chkHeader = new() { Text = "带上说明头（告诉 AI 这段是什么）", AutoSize = true };
    private readonly CheckedListBox _categories = new();
    private readonly Label _stats = new() { AutoSize = true };
    private readonly Label _hint = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly Button _btnCopy = new() { Text = "复制到剪贴板", Width = 130 };
    private readonly Button _btnSave = new() { Text = "另存为文件…", Width = 120 };
    private readonly Button _btnClose = new() { Text = "关闭", Width = 90 };
    private readonly Button _btnSelectNone = new() { Text = "全不选", Width = 78 };
    private readonly Button _btnSelectAll = new() { Text = "全选", Width = 68 };

    private ExportBundle _bundle = new();

    public ExportDialog(MemoryRepository repo, IReadOnlyList<MemoryEntry> entries)
    {
        _repo = repo;
        _entries = entries;

        BuildUi();
        Load += (_, _) => Rebuild();
    }

    private void BuildUi()
    {
        Text = "导出给网页版 AI";
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        var work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
        var width = Math.Min(880, Math.Max(680, work.Width - 120));
        var height = Math.Min(700, Math.Max(520, work.Height - 120));
        Size = new Size(width, height);
        MinimumSize = new Size(640, 480);
        Font = new Font("Microsoft YaHei UI", 9F);

        // ---------------- 顶部：说明 + 选项 + 类别筛选
        // 用「自上而下的自动布局」：外层纵向 FlowLayoutPanel(AutoSize) 堆三块，
        // 每块自己决定高度，整块再按内容自动撑高。
        // 之前把说明文字的高度硬编码成 40px，而两行中文实测需要 48px，
        // 结果文字溢出压住下方复选框。手工测高这类事交给布局引擎，别再硬编码。
        var headerHost = new Panel
        {
            Dock = DockStyle.Top,
            Padding = new Padding(14, 10, 14, 8),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        var headerStack = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        var explain = new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6),
            Text = "下面这段文字可以直接复制，粘进 ChatGPT / Claude / DeepSeek 等网页对话框。\n" +
                   "格式是纯文本，不带标记注释，粘贴后即可用。",
        };

        var optionsRow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 8),
        };
        _chkPreferences.Checked = true;
        _chkHeader.Checked = true;
        foreach (var box in new[] { _chkHeader, _chkPreferences })
        {
            box.Margin = new Padding(0, 4, 24, 0);
            box.CheckedChanged += (_, _) => Rebuild();
            optionsRow.Controls.Add(box);
        }

        // ---------------- 类别筛选
        // 布局：顶部一行提示 + 右侧两个按钮 + 中间列表填满剩余。
        // 用绝对坐标算宽度在不同 DPI / 字号下会被挤扁，这里改成 Dock 组合。
        var categoryRow = new Panel { Dock = DockStyle.Top, Height = 76 };

        var categoryLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 20,
            Text = "只导出这些类别（不勾 = 全部）：",
        };

        var categoryButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 160,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(6, 6, 0, 0),
        };
        _btnSelectAll.Width = 74;
        _btnSelectAll.Height = 26;
        _btnSelectAll.Click += (_, _) => SetAllCategories(true);
        _btnSelectNone.Width = 74;
        _btnSelectNone.Height = 26;
        _btnSelectNone.Click += (_, _) => SetAllCategories(false);
        categoryButtons.Controls.Add(_btnSelectAll);
        categoryButtons.Controls.Add(_btnSelectNone);

        _categories.Dock = DockStyle.Fill;
        _categories.CheckOnClick = true;
        _categories.MultiColumn = true;
        _categories.ColumnWidth = 148;
        _categories.IntegralHeight = false;
        _categories.BorderStyle = BorderStyle.FixedSingle;
        _categories.BackColor = Color.White;
        _categories.ItemCheck += (_, _) => BeginInvoke(Rebuild);

        var categoryListHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 4, 6, 0) };
        categoryListHost.Controls.Add(_categories);

        // Dock 的叠放顺序：Right 要先加，Fill 最后加
        categoryRow.Controls.Add(categoryListHost);
        categoryRow.Controls.Add(categoryButtons);
        categoryRow.Controls.Add(categoryLabel);

        // 组装：纵向堆叠，每块自己决定高度
        categoryRow.Height = 84;
        categoryRow.Margin = new Padding(0);
        categoryRow.AutoSize = false;
        headerStack.Controls.Add(explain);
        headerStack.Controls.Add(optionsRow);
        headerStack.Controls.Add(categoryRow);
        headerHost.Controls.Add(headerStack);

        // 说明文字要换行：宽度跟着窗口走，高度交给 AutoSize
        void ResizeExplain()
        {
            var available = headerHost.ClientSize.Width - headerHost.Padding.Horizontal;
            if (available > 60) explain.MaximumSize = new Size(available, 0);
            if (available > 60)
            {
                categoryRow.Width = available;
                optionsRow.Width = available;
            }
        }
        headerHost.Resize += (_, _) => ResizeExplain();
        ResizeExplain();

        // ---------------- 中部：预览
        _preview.Dock = DockStyle.Fill;
        _preview.Multiline = true;
        _preview.ReadOnly = true;
        _preview.ScrollBars = ScrollBars.Both;
        _preview.WordWrap = false;
        _preview.BackColor = Color.White;
        _preview.Font = new Font("Consolas", 9.5F);
        _preview.BorderStyle = BorderStyle.FixedSingle;

        var previewHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 4, 14, 4) };
        previewHost.Controls.Add(_preview);

        // ---------------- 底部：统计 + 按钮
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 54, Padding = new Padding(14, 6, 14, 10) };

        _stats.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        _stats.Location = new Point(14, 18);
        _hint.Location = new Point(300, 18);

        _btnCopy.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _btnSave.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _btnCopy.Location = new Point(width - 14 - 130 - 8 - 120 - 8 - 90, 12);
        _btnSave.Location = new Point(width - 14 - 120 - 8 - 90, 12);
        _btnClose.Location = new Point(width - 14 - 90, 12);

        _btnCopy.Click += (_, _) => DoCopy();
        _btnSave.Click += (_, _) => DoSave();
        _btnClose.Click += (_, _) => Close();

        bottom.Controls.AddRange(new Control[] { _stats, _hint, _btnCopy, _btnSave, _btnClose });

        Controls.Add(previewHost);
        Controls.Add(bottom);
        Controls.Add(headerHost);

        AcceptButton = _btnCopy;
        CancelButton = _btnClose;
    }

    private void SetAllCategories(bool selected)
    {
        for (var i = 0; i < _categories.Items.Count; i++)
            _categories.SetItemChecked(i, selected);
    }

    /// <summary>按当前选项重新生成预览。</summary>
    private void Rebuild()
    {
        RefreshCategoryListIfNeeded();

        var chosen = _categories.CheckedItems.Count == 0
            ? null
            : _categories.CheckedItems.Cast<string>().ToList();

        var agentsText = File.Exists(_repo.Config.AgentsPath)
            ? TryRead(_repo.Config.AgentsPath)
            : null;

        _bundle = MemoryExporter.Build(
            _entries,
            agentsText,
            new MemoryExporter.Options
            {
                // 记录时间固定不导出：对 AI 判断帮助小，且会明显拉长体量
                IncludeTime = false,
                IncludePreferences = _chkPreferences.Checked,
                IncludeHeader = _chkHeader.Checked,
                OnlyCategories = chosen,
            });

        var caret = _preview.SelectionStart;
        _preview.Text = _bundle.Text;
        _preview.SelectionStart = Math.Min(caret, _preview.TextLength);

        _stats.Text = $"导出 {_bundle.EntryCount} 条 ／ {_bundle.CategoryCount} 个类别";
        _hint.Text = "体量：" + _bundle.SizeSummary;

        if (_bundle.EstimatedTokens > 4000)
            _hint.ForeColor = Color.Firebrick;
        else if (_bundle.EstimatedTokens > 1500)
            _hint.ForeColor = Color.DarkOrange;
        else
            _hint.ForeColor = SystemColors.GrayText;
    }

    private bool _categoryListBuilt;

    private void RefreshCategoryListIfNeeded()
    {
        if (_categoryListBuilt) return;
        _categoryListBuilt = true;

        var names = CategoryCatalog.MergeWith(
            _entries.Select(e => e.Category).Where(c => !string.IsNullOrWhiteSpace(c)));

        foreach (var name in names) _categories.Items.Add(name);
    }

    private void DoCopy()
    {
        try
        {
            if (_bundle.Text.Length == 0)
            {
                MessageBox.Show(this, "当前选项下没有可导出的内容。", "导出", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Clipboard.SetText(_bundle.Text);
            _hint.Text = $"已复制 {_bundle.Characters:N0} 字符，去对话框里 Ctrl+V 即可。";
            _hint.ForeColor = Color.SeaGreen;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "复制失败：" + ex.Message, "导出", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void DoSave()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "导出记忆",
            Filter = "文本文件 (*.txt)|*.txt|Markdown (*.md)|*.md|所有文件 (*.*)|*.*",
            FileName = $"记忆导出-{DateTime.Now:yyyyMMdd-HHmm}.txt",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            // 用 UTF-8 无 BOM 写，避免某些编辑器读到开头的 \uFEFF
            File.WriteAllText(dialog.FileName, _bundle.Text, new UTF8Encoding(false));
            _hint.Text = "已保存：" + dialog.FileName;
            _hint.ForeColor = Color.SeaGreen;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "保存失败：" + ex.Message, "导出", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static string? TryRead(string path)
    {
        try { return Utf8.ReadAllText(path); }
        catch { return null; }
    }
}
