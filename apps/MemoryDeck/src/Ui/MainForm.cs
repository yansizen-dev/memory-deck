using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using MemoryDeck.Core;

namespace MemoryDeck.Ui;

public sealed class MainForm : Form
{
    private MemoryRepository _repo;
    private StorageConfig _config;
    private RepoSnapshot _snapshot = new();
    private IReadOnlyList<MemoryEntry> _view = Array.Empty<MemoryEntry>();
    private EditorMode _mode = EditorMode.None;
    private MemoryEntry? _current;

    private bool _suppressEvents;
    private string _lastFingerprint = "";

    // 控件
    private readonly MenuStrip _menu = new();
    private readonly ToolStripMenuItem _miDataDirectory = new("数据目录：—");
    private readonly ToolStripMenuItem _miSyncAgents = new("立即同步 AGENTS.md");
    private readonly ToolStripMenuItem _miBackup = new("立即备份");
    private readonly ToolStripMenuItem _miOpenBackups = new("打开备份目录");
    private readonly ToolStripMenuItem _miOpenData = new("打开数据目录");
    private readonly ToolStripMenuItem _miHelp = new("使用说明");
    private readonly ToolStripMenuItem _miAbout = new("关于 MemoryDeck");

    private readonly Panel _topPanel = new() { Dock = DockStyle.Top, Height = 46, Padding = new Padding(12, 9, 12, 4) };
    private readonly TextBox _txtSearch = new() { Width = 260 };
    private readonly ComboBox _cboCategory = new() { Width = 130, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _btnClearSearch = new() { Text = "清空", Width = 60 };
    private readonly Label _lblCount = new() { AutoSize = true, Text = "0 条" };

    private readonly SplitContainer _split = new()
    {
        Dock = DockStyle.Fill,
        Orientation = Orientation.Horizontal,
        SplitterWidth = 6,
        // 注意：SplitterDistance 不能在这里设。窗口还没有尺寸时设置会抛
        // InvalidOperationException（SplitterDistance 必须在 Panel1MinSize 与
        // Width - Panel2MinSize 之间），表现为一打开就闪退。
        // 统一放到 OnLoad 里、控件完成布局之后再设。
    };

    private readonly DataGridView _grid = new();
    private readonly DataGridViewTextBoxColumn _colId = new() { HeaderText = "ID", Width = 92, ReadOnly = true };
    private readonly DataGridViewTextBoxColumn _colCategory = new() { HeaderText = "类别", Width = 96, ReadOnly = true };
    private readonly DataGridViewTextBoxColumn _colContent = new() { HeaderText = "内容", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true };

    /// <summary>表格的留白容器：给 DataGridView 一圈呼吸空间，避免内容贴着窗口边缘。</summary>
    private readonly Panel _gridHost = new() { Dock = DockStyle.Fill, Padding = new Padding(1, 0, 1, 0) };

    /// <summary>右键菜单：来源与记录时间等细节不再常显在列里，改从这里按需查看。</summary>
    private readonly ContextMenuStrip _gridMenu = new();
    private readonly ToolStripMenuItem _miShowDetail = new("查看完整信息…");
    private readonly ToolStripMenuItem _miCopyContent = new("复制内容");
    private readonly ToolStripMenuItem _miCopyRow = new("复制整条（制表符分隔）");
    private readonly ToolStripMenuItem _miEditHere = new("编辑此条");
    private readonly ToolStripMenuItem _miDeleteHere = new("删除此条");

    /// <summary>详情区可折叠，表格为空时自动收起，把空间还给列表。</summary>
    private readonly ToolStripMenuItem _miToggleEditor = new("收起/展开详情区");

    private readonly Panel _editor = new() { Dock = DockStyle.Fill, Padding = new Padding(12, 8, 12, 10) };
    private readonly Label _lblMode = new() { AutoSize = true, Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold) };
    private readonly Label _lblHint = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    private readonly TextBox _txtId = new() { Width = 90 };
    private readonly ComboBox _cboEditCategory = new() { Width = 130 };
    private readonly TextBox _txtSource = new() { Width = 200 };
    private readonly TextBox _txtContent = new();
    private readonly Button _btnPrimary = new() { Text = "新增", Width = 88 };
    private readonly Button _btnDelete = new() { Text = "删除", Width = 88 };
    private readonly Button _btnReload = new() { Text = "刷新", Width = 88 };

    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _statusLeft = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _statusCount = new() { Text = "0 条" };
    private readonly ToolStripStatusLabel _statusPath = new() { Text = "" };
    private readonly System.Windows.Forms.Timer _watch = new() { Interval = 1500 };

    private enum EditorMode { None, View, New, Edit }

    public MainForm(StorageConfig config)
    {
        _config = config;
        _repo = new MemoryRepository(_config);

        BuildUi();
        _watch.Tick += (_, _) => RunGuarded("外部变更监视", WatchForExternalChanges);
        _watch.Start();
    }

    /// <summary>加载时把分隔条位置放到安全区间，避免构造期抛异常。</summary>
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        try
        {
            // 容器此刻已有真实尺寸，可以安全地设置精确分隔位置
            _split.Panel2MinSize = 200;

            var available = _split.Height - _split.SplitterWidth;
            if (available > _split.Panel1MinSize + _split.Panel2MinSize)
            {
                var target = (int)(available * 0.45);
                target = Math.Max(target, _split.Panel1MinSize);
                target = Math.Min(target, available - _split.Panel2MinSize);
                if (target > 0) _split.SplitterDistance = target;
            }
        }
        catch (InvalidOperationException)
        {
            // 极端尺寸下放弃调整，用默认位置继续。
        }

        ReloadAll(selectFirst: true);
    }

    // ================================================================ 界面构建

    private void BuildUi()
    {
        try
        {
            BuildUiCore();
        }
        catch (Exception ex)
        {
            try
            {
                var dir = Path.Combine(Path.GetTempPath(), "MemoryDeck");
                Directory.CreateDirectory(dir);
                File.AppendAllText(
                    Path.Combine(dir, "buildui.log"),
                    $"[{DateTime.Now:HH:mm:ss}] BuildUi 失败：{ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}{Environment.NewLine}",
                    new UTF8Encoding(false));
            }
            catch
            {
                // 日志失败不影响抛出
            }

            throw;
        }
    }

    private void BuildUiCore()
    {
        Text = "MemoryDeck — 记忆卡片管理器";
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9F);

        // 窗口尺寸按可用屏幕自适应：屏幕小的时候别把窗口撑到屏幕外面去。
        var work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
        var width = Math.Min(1120, Math.Max(760, work.Width - 80));
        var height = Math.Min(720, Math.Max(520, work.Height - 80));
        Size = new Size(width, height);
        MaximumSize = new Size(work.Width, work.Height);
        MinimumSize = new Size(Math.Min(940, width), Math.Min(600, height));
        KeyPreview = true;

        BuildMenu();

        // ---- 顶部工具条
        _txtSearch.PlaceholderText = "搜索 ID / 类别 / 内容 / 来源";
        _txtSearch.TextChanged += (_, _) => ApplyFilter();
        _cboCategory.SelectedIndexChanged += (_, _) => { if (!_suppressEvents) ApplyFilter(); };
        _btnClearSearch.Click += (_, _) => { _txtSearch.Clear(); _cboCategory.SelectedIndex = 0; };

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            // 窄屏时允许换行，避免控件被挤出可视区
            WrapContents = true,
            AutoSize = false,
        };
        flow.Controls.Add(new Label { Text = "搜索", AutoSize = true, Margin = new Padding(0, 7, 4, 0) });
        flow.Controls.Add(_txtSearch);
        flow.Controls.Add(_btnClearSearch);
        flow.Controls.Add(new Label { Text = "类别", AutoSize = true, Margin = new Padding(16, 7, 4, 0) });
        flow.Controls.Add(_cboCategory);
        flow.Controls.Add(_lblCount);
        _lblCount.Margin = new Padding(16, 7, 0, 0);
        _topPanel.Controls.Add(flow);

        // ---- 表格
        // 只常显三列：ID / 类别 / 内容。
        // 来源与记录时间移出列，改从右键菜单「查看完整信息」按需看——
        // 这两项日常扫视用不到，却各占一百多像素，是密度和观感的主要来源。
        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.ReadOnly = true;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.RowHeadersVisible = false;
        _grid.BackgroundColor = Color.White;
        _grid.BorderStyle = BorderStyle.None;
        _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _grid.GridColor = Color.FromArgb(238, 240, 243);
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(247, 248, 250);
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(90, 96, 105);
        _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(247, 248, 250);
        _grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Color.FromArgb(90, 96, 105);
        _grid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 0, 0);
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _grid.ColumnHeadersHeight = 34;
        _grid.RowTemplate.Height = 32;
        _grid.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(226, 238, 254);
        _grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(20, 24, 30);
        _grid.DefaultCellStyle.ForeColor = Color.FromArgb(32, 36, 42);
        // 用「选中高亮」代替斑马纹：交替底色在浅色主题里反而更显杂
        _grid.AlternatingRowsDefaultCellStyle.BackColor = Color.White;
        _grid.AutoGenerateColumns = false;
        _grid.Columns.AddRange(_colId, _colCategory, _colContent);
        _grid.SelectionChanged += (_, _) => RunGuarded("表格选中变化", OnGridSelectionChanged);
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) BeginEditSelected(); };
        _grid.KeyDown += Grid_KeyDown;
        _grid.CellMouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right || e.RowIndex < 0) return;
            if (e.RowIndex < _grid.Rows.Count) _grid.Rows[e.RowIndex].Selected = true;
        };
        _grid.CellToolTipTextNeeded += (_, e) =>
        {
            e.ToolTipText = "";
            if (e.RowIndex < 0 || e.RowIndex >= _view.Count) return;
            if (e.RowIndex >= _grid.Rows.Count) return;
            e.ToolTipText = _view[e.RowIndex].Content;
        };

        BuildGridMenu();
        _grid.ContextMenuStrip = _gridMenu;
        _gridHost.Controls.Add(_grid);

        // ---- 编辑区
        BuildEditor();

        _split.Panel1.Controls.Add(_gridHost);
        _split.Panel2.Controls.Add(_editor);

        // 面板最小尺寸必须用「安全范围内」的值：
        // Panel2MinSize 的 setter 会顺手调用 ApplyPanel2MinSize 重算 SplitterDistance，
        // 此时容器还没有尺寸，设大了会直接抛 InvalidOperationException。
        // 真正想要的分隔位置统一放到 OnLoad 里、控件完成布局之后再设。
        // 另外先写 Panel1MinSize 再写小的 Panel2MinSize，保证两次赋值都在合法区间。
        _split.Panel1MinSize = 120;
        _split.Panel2MinSize = 25;

        // ---- 状态栏
        _status.Items.AddRange(new ToolStripItem[] { _statusLeft, _statusCount, _statusPath });
        _status.SizingGrip = true;
        _statusPath.ForeColor = SystemColors.GrayText;
        _statusPath.BorderSides = ToolStripStatusLabelBorderSides.Left;

        // 停靠顺序：先加 Fill，再加 Top / Bottom，避免遮挡
        Controls.Add(_split);
        Controls.Add(_topPanel);
        Controls.Add(_menu);
        Controls.Add(_status);
        MainMenuStrip = _menu;

        KeyDown += MainForm_KeyDown;
    }

    private void BuildMenu()
    {
        var file = new ToolStripMenuItem("文件(&F)");
        file.DropDownItems.Add(NewItem("新建记忆(&N)\tCtrl+N", (_, _) => BeginNew()));
        file.DropDownItems.Add(NewItem("刷新(&R)\tF5", (_, _) => ReloadAll(selectFirst: true)));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(NewItem("打开数据目录(&O)", (_, _) => OpenInExplorer(_config.DataDirectory)));
        file.DropDownItems.Add(NewItem("打开 MEMORY.md", (_, _) => OpenFileOrDefault(_config.MemoryPath)));
        file.DropDownItems.Add(NewItem("打开 AGENTS.md", (_, _) => OpenFileOrDefault(_config.AgentsPath)));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(NewItem("退出(&X)", (_, _) => Close()));

        var data = new ToolStripMenuItem("数据(&D)");
        _miDataDirectory.Click += (_, _) => ChangeDataDirectory();
        data.DropDownItems.Add(_miDataDirectory);
        data.DropDownItems.Add(new ToolStripSeparator());
        _miBackup.Click += (_, _) => DoBackup();
        _miOpenBackups.Click += (_, _) => OpenInExplorer(_config.AutoBackupDirectory);
        data.DropDownItems.Add(_miBackup);
        data.DropDownItems.Add(_miOpenBackups);
        data.DropDownItems.Add(new ToolStripSeparator());
        _miSyncAgents.Click += (_, _) => DoSyncAgents();
        data.DropDownItems.Add(_miSyncAgents);

        var help = new ToolStripMenuItem("帮助(&H)");
        _miHelp.Click += (_, _) => ShowHelp();
        _miAbout.Click += (_, _) => ShowAbout();
        help.DropDownItems.Add(_miHelp);
        help.DropDownItems.Add(_miAbout);

        _menu.Items.AddRange(new ToolStripItem[] { file, data, help });
    }

    private static ToolStripMenuItem NewItem(string text, EventHandler handler)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += handler;
        return item;
    }

    private void BuildEditor()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = 4,
            AutoSize = false,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _lblMode.Text = "新增记忆";
        _lblHint.Text = "填写后点击「保存」，写入 MEMORY.md 并同步 AGENTS.md";

        var header = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        header.Controls.Add(_lblMode);
        _lblHint.Margin = new Padding(14, 5, 0, 0);
        header.Controls.Add(_lblHint);

        layout.Controls.Add(header, 0, 0);
        layout.SetColumnSpan(header, 6);

        layout.Controls.Add(new Label { Text = "ID", AutoSize = true, Margin = new Padding(0, 8, 0, 0) }, 0, 1);
        _txtId.ReadOnly = true;
        _txtId.BackColor = SystemColors.Control;
        layout.Controls.Add(_txtId, 1, 1);

        layout.Controls.Add(new Label { Text = "类别", AutoSize = true, Margin = new Padding(0, 8, 0, 0) }, 2, 1);
        _cboEditCategory.DropDownStyle = ComboBoxStyle.DropDown;
        layout.Controls.Add(_cboEditCategory, 3, 1);

        layout.Controls.Add(new Label { Text = "来源", AutoSize = true, Margin = new Padding(0, 8, 0, 0) }, 4, 1);
        layout.Controls.Add(_txtSource, 5, 1);

        layout.Controls.Add(new Label { Text = "内容", AutoSize = true, Margin = new Padding(0, 8, 0, 0) }, 0, 2);
        _txtContent.Multiline = true;
        _txtContent.ScrollBars = ScrollBars.Vertical;
        _txtContent.AcceptsReturn = false;
        _txtContent.Dock = DockStyle.Fill;
        layout.Controls.Add(_txtContent, 1, 2);
        layout.SetColumnSpan(_txtContent, 5);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        _txtContent.KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                SaveEditor();
            }
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 6, 0, 0),
        };
        _btnPrimary.Click += (_, _) => SaveEditor();
        _btnDelete.Click += (_, _) => DeleteSelected();
        _btnReload.Click += (_, _) => { ReloadAll(selectFirst: true); BeginNew(); };
        buttons.Controls.Add(_btnPrimary);
        buttons.Controls.Add(_btnDelete);
        buttons.Controls.Add(_btnReload);
        layout.Controls.Add(buttons, 0, 3);
        layout.SetColumnSpan(buttons, 6);

        _editor.Controls.Add(layout);
    }

    // ================================================================ 数据流

    private void ReloadAll(bool selectFirst)
    {
        var selectedId = CurrentSelectedEntry()?.Id;

        _snapshot = _repo.Load();
        _lastFingerprint = _snapshot.Fingerprint;

        RefreshCategoryFilter();
        ApplyFilter();

        if (selectFirst && _view.Count > 0)
            SelectRowById(selectedId ?? _view[0].Id);

        UpdateStatus();

        // 类别下拉重建时会重写编辑区文本，这里按当前选中项重新灌一次，保证显示与选中一致
        var current = CurrentSelectedEntry();
        if (current is not null)
        {
            PopulateEditor(current);
            SetEditorMode(EditorMode.View);
        }

        if (_snapshot.Warnings.Count > 0)
            SetStatus("读取告警：" + string.Join("；", _snapshot.Warnings), warning: true);
    }

    private void ApplyFilter()
    {
        var query = _txtSearch.Text.Trim();
        var category = (_cboCategory.SelectedItem as CategoryChoice)?.Name;

        IEnumerable<MemoryEntry> items = _snapshot.Entries;
        if (!string.IsNullOrEmpty(query))
        {
            items = items.Where(e =>
                Contains(e.Id, query) || Contains(e.Category, query) ||
                Contains(e.Content, query) || Contains(e.Source, query));
        }

        if (!string.IsNullOrEmpty(category) && category != FilterAllLabel)
            items = items.Where(e => string.Equals(e.Category, category, StringComparison.OrdinalIgnoreCase));

        _view = items.ToList();

        _suppressEvents = true;
        try
        {
            _grid.Rows.Clear();
            foreach (var entry in _view)
            {
                // 只有三列：来源与记录时间见右键菜单「查看完整信息」
                var index = _grid.Rows.Add(entry.Id, entry.Category, entry.Content);
                _grid.Rows[index].Tag = entry.Id;
            }

            if (_grid.Rows.Count > 0 && _grid.SelectedRows.Count == 0)
                _grid.Rows[0].Selected = true;
        }
        finally
        {
            _suppressEvents = false;
        }

        var suffix = _view.Count == _snapshot.Entries.Count ? "" : $"（共 {_snapshot.Entries.Count} 条）";
        _lblCount.Text = $"筛选 {_view.Count} 条{suffix}";
        _statusCount.Text = $"{_view.Count} / {_snapshot.Entries.Count} 条";

        // 没有条目时自动收起详情区，把空间让给列表
        if (_view.Count == 0 && _split.Panel2Collapsed == false) SetEditorCollapsed(true);

        if (_grid.SelectedRows.Count > 0) OnGridSelectionChanged();
    }

    private const string FilterAllLabel = CategoryCatalog.AllLabel;

    /// <summary>
    /// 重建筛选下拉：预设类别在前，数据里出现过的额外类别在后。
    /// 这样即使一条记忆都没有，下拉里也始终有可选项。
    /// </summary>
    private void RefreshCategoryFilter()
    {
        var previous = (_cboCategory.SelectedItem as CategoryChoice)?.Name;

        var existing = _snapshot.Entries
            .Select(e => e.Category)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .ToList();

        var merged = CategoryCatalog.MergeWith(existing);
        var usedNames = new HashSet<string>(
            existing.Select(c => c.Trim()),
            StringComparer.OrdinalIgnoreCase);

        _suppressEvents = true;
        try
        {
            _cboCategory.Items.Clear();
            foreach (var name in merged)
            {
                // 给在用的类别标一个计数，一眼能看出数据分布
                var label = usedNames.Contains(name)
                    ? $"{name}（{existing.Count(c => string.Equals(c.Trim(), name, StringComparison.OrdinalIgnoreCase))}）"
                    : name;
                _cboCategory.Items.Add(new CategoryChoice(name, label));
            }

            if (_cboCategory.Items.Count > 0) _cboCategory.Items.Insert(0, CategoryChoice.All);

            var restoreIndex = 0;
            if (previous is not null)
            {
                for (var i = 0; i < _cboCategory.Items.Count; i++)
                {
                    if (_cboCategory.Items[i] is CategoryChoice choice && choice.Name == previous)
                    {
                        restoreIndex = i;
                        break;
                    }
                }
            }

            _cboCategory.SelectedIndex = restoreIndex;
        }
        finally
        {
            _suppressEvents = false;
        }

        RefreshEditCategoryItems(existing);
    }

    private void OnFilterCategoryChanged(object? sender, EventArgs e) => ApplyFilter();

    /// <summary>编辑区的类别下拉：预设 + 已有，且允许自由输入新类别。</summary>
    private void RefreshEditCategoryItems(IEnumerable<string> existing)
    {
        var typed = _cboEditCategory.Text;

        _cboEditCategory.Items.Clear();
        foreach (var name in CategoryCatalog.MergeWith(existing))
        {
            var description = CategoryCatalog.DescribeOf(name);
            _cboEditCategory.Items.Add(description.Length == 0 ? name : $"{name} — {description}");
        }

        // 下拉项带了说明文字，重新选中时必须还原成纯类别名
        _cboEditCategory.Text = typed;
    }

    /// <summary>筛选下拉里的一个选项：显示文本可以带计数，实际值始终是纯类别名。</summary>
    private sealed record CategoryChoice(string Name, string Label)
    {
        public static readonly CategoryChoice All = new(CategoryCatalog.AllLabel, CategoryCatalog.AllLabel);

        public override string ToString() => Label;
    }

    private void UpdateStatus()
    {
        _miDataDirectory.Text = "数据目录：" + _config.DataDirectory;
        _statusPath.Text = _config.MemoryPath;

        var memoryState = _snapshot.MemoryExists ? "MEMORY.md 已就绪" : "MEMORY.md 尚未创建";
        var agentsState = _snapshot.AgentsExists ? "AGENTS.md 已就绪" : "AGENTS.md 尚未创建";
        SetStatus($"{memoryState} ｜ {agentsState}");
    }

    private void SetStatus(string text, bool warning = false)
    {
        _statusLeft.Text = text;
        _statusLeft.ForeColor = warning ? Color.Firebrick : SystemColors.ControlText;
    }

    private void OnGridSelectionChanged()
    {
        if (_suppressEvents) return;
        if (_mode is EditorMode.New or EditorMode.Edit) return;

        // 重载过程中表格会被清空，此时行索引可能已经越界，必须加界检查
        var entry = CurrentSelectedEntry();
        if (entry is null)
        {
            SetEditorMode(EditorMode.None);
            if (_grid.SelectedRows.Count == 0) ClearEditor();
            return;
        }

        PopulateEditor(entry);
        SetEditorMode(EditorMode.View);
    }

    private void ClearEditor()
    {
        _current = null;
        _suppressEvents = true;
        try
        {
            _txtId.Text = "";
            _cboEditCategory.Text = "";
            _txtSource.Text = "";
            _txtContent.Text = "";
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    /// <summary>
    /// 统一的异常护栏：界面事件回调里抛出的异常会直接终止进程，
    /// 所以任何可能失败的 UI 回调都必须经过这里，把异常转成状态栏提示。
    /// </summary>
    /// <summary>
    /// 供自检使用：触发一次与「外部修改文件后自动刷新」等价的完整重载流程。
    /// 这条路径曾经在表格清空、行索引越界时直接把进程干掉，必须有回归覆盖。
    /// </summary>
    internal void ReloadForTest() => RunGuarded("重载（自检）", () => ReloadAll(selectFirst: true));

    /// <summary>供自检使用：读取当前表格行数。</summary>
    internal int GridRowCountForTest => _grid.Rows.Count;

    /// <summary>供自检使用：读取当前快照条目数。</summary>
    internal int EntryCountForTest => _snapshot.Entries.Count;

    /// <summary>供自检使用：读取筛选下拉的选项数量（含「全部」）。</summary>
    internal int FilterCategoryCountForTest => _cboCategory.Items.Count;

    /// <summary>供自检使用：读取编辑区类别下拉还原出的纯类别名。</summary>
    internal string EditorCategoryNameForTest => ParseCategoryName(_cboEditCategory.Text);

    private void RunGuarded(string what, Action body)
    {
        try
        {
            body();
        }
        catch (Exception ex)
        {
            try
            {
                var dir = Path.Combine(Path.GetTempPath(), "MemoryDeck");
                Directory.CreateDirectory(dir);
                File.AppendAllText(
                    Path.Combine(dir, "ui-errors.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {what} 异常：{ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}{Environment.NewLine}{Environment.NewLine}",
                    new UTF8Encoding(false));
            }
            catch
            {
                // 日志失败也不能再抛
            }

            try
            {
                SetStatus($"{what} 出错（已忽略并记录）：{ex.Message}", warning: true);
            }
            catch
            {
                // 连状态栏都失败就彻底放弃
            }
        }
    }

    private MemoryEntry? CurrentSelectedEntry()
    {
        if (_grid.SelectedRows.Count == 0) return null;

        // 选中行的 Tag 保存的是条目 ID；用 ID 去快照里找，不依赖行索引，
        // 这样即使表格正在重载、行与 _view 短暂不同步也不会取错或越界。
        if (_grid.SelectedRows[0].Tag is not string id) return null;
        if (string.IsNullOrWhiteSpace(id)) return null;

        return _snapshot.Entries.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    private void SelectRowById(string id)
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Tag is string tag && string.Equals(tag, id, StringComparison.OrdinalIgnoreCase))
            {
                row.Selected = true;
                row.Cells[0].Selected = true;
                return;
            }
        }

        if (_grid.Rows.Count > 0) _grid.Rows[0].Selected = true;
    }

    // ================================================================ 编辑态

    private void BeginNew()
    {
        _current = null;
        var nextId = _repo.NextId(_snapshot.Entries);

        PopulateEditor(new MemoryEntry
        {
            Id = nextId,
            Category = ParseCategoryName(_cboEditCategory.Text),
            Content = "",
            Source = Environment.UserName,
            Time = "",
        });

        SetEditorMode(EditorMode.New);
        _txtContent.Focus();
    }

    private void BeginEditSelected()
    {
        var entry = CurrentSelectedEntry();
        if (entry is null) return;

        _current = entry;
        PopulateEditor(entry);
        SetEditorMode(EditorMode.Edit);
        _txtContent.Focus();
        _txtContent.SelectionStart = _txtContent.TextLength;
    }

    /// <summary>
    /// 从类别框的文本里取出纯类别名。
    /// 下拉项形如「身份 — 我是谁：背景、角色、当前状态」，
    /// 用户手输时则是纯名字，两种情况都要正确还原。
    /// </summary>
    private static string ParseCategoryName(string? text)
    {
        var raw = (text ?? "").Trim();
        if (raw.Length == 0) return "";

        var separator = raw.IndexOf(" — ", StringComparison.Ordinal);
        return separator > 0 ? raw[..separator].Trim() : raw;
    }

    private void PopulateEditor(MemoryEntry entry)
    {
        _suppressEvents = true;
        try
        {
            _txtId.Text = entry.Id;
            _cboEditCategory.Text = entry.Category;
            _txtSource.Text = entry.Source;
            _txtContent.Text = entry.Content;
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    /// <summary>
    /// 收起 / 展开详情区。收起后表格独占整个高度，
    /// 解决「两条数据时中间一大片空白」的观感问题。
    /// </summary>
    private void SetEditorCollapsed(bool collapsed)
    {
        if (_split.Panel2Collapsed == collapsed) return;

        try
        {
            _split.Panel2Collapsed = collapsed;
        }
        catch (InvalidOperationException)
        {
            // 极端尺寸下放弃折叠，保持现状
            return;
        }

        _statusLeft.Text = collapsed ? "详情区已收起（Ctrl+D 或右键菜单可展开）" : "";
        _statusLeft.ForeColor = SystemColors.ControlText;
    }

    private void BuildGridMenu()
    {
        _miShowDetail.Click += (_, _) => ShowDetailDialog();
        _miCopyContent.Click += (_, _) =>
        {
            var entry = CurrentSelectedEntry();
            if (entry is null) return;
            CopyToClipboard(entry.Content);
        };
        _miCopyRow.Click += (_, _) =>
        {
            var entry = CurrentSelectedEntry();
            if (entry is null) return;
            CopyToClipboard($"{entry.Id}\t{entry.Category}\t{entry.Content}\t{entry.Source}\t{entry.Time}");
        };
        _miEditHere.Click += (_, _) => BeginEditSelected();
        _miDeleteHere.Click += (_, _) => DeleteSelected();
        _miToggleEditor.Click += (_, _) => SetEditorCollapsed(!_split.Panel2Collapsed);

        _gridMenu.Items.AddRange(new ToolStripItem[]
        {
            _miShowDetail,
            new ToolStripSeparator(),
            _miCopyContent,
            _miCopyRow,
            new ToolStripSeparator(),
            _miEditHere,
            _miDeleteHere,
            new ToolStripSeparator(),
            _miToggleEditor,
        });

        _gridMenu.Opening += (_, _) =>
        {
            var hasSelection = CurrentSelectedEntry() is not null;
            _miShowDetail.Enabled = hasSelection;
            _miCopyContent.Enabled = hasSelection;
            _miCopyRow.Enabled = hasSelection;
            _miEditHere.Enabled = hasSelection;
            _miDeleteHere.Enabled = hasSelection;
        };
    }

    private void CopyToClipboard(string text)
    {
        try
        {
            if (string.IsNullOrEmpty(text)) return;
            Clipboard.SetText(text);
            SetStatus("已复制到剪贴板。");
        }
        catch (Exception ex)
        {
            SetStatus("复制失败：" + ex.Message, warning: true);
        }
    }

    /// <summary>
    /// 完整信息弹窗。来源与记录时间从表格列里移出来后，在这里按需查看。
    /// </summary>
    private void ShowDetailDialog()
    {
        var entry = CurrentSelectedEntry();
        if (entry is null) return;

        var text =
            $"ID：        {entry.Id}{Environment.NewLine}" +
            $"类别：      {entry.Category}{Environment.NewLine}" +
            $"来源：      {entry.Source}{Environment.NewLine}" +
            $"记录时间：  {entry.Time}{Environment.NewLine}" +
            $"{new string('─', 46)}{Environment.NewLine}" +
            $"{entry.Content}";

        MessageBox.Show(this, text, "记忆详情 — " + entry.Id, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void SetEditorMode(EditorMode mode)
    {
        _mode = mode;

        // 要编辑或查看就必须把详情区展开（用户可能之前手动收起过）
        if (mode is EditorMode.View or EditorMode.New or EditorMode.Edit)
            SetEditorCollapsed(false);

        switch (mode)
        {
            case EditorMode.None:
                _lblMode.Text = "未选择条目";
                _lblHint.Text = "点击表格中的一行查看详情，或按 Ctrl+N 新增记忆";
                _btnPrimary.Text = "新增";
                _btnPrimary.Enabled = true;
                _btnDelete.Enabled = false;
                _txtContent.ReadOnly = true;
                _cboEditCategory.Enabled = false;
                _txtSource.ReadOnly = true;
                break;

            case EditorMode.View:
                _lblMode.Text = "查看条目";
                _lblHint.Text = "双击表格行或点击「编辑」可修改";
                _btnPrimary.Text = "编辑";
                _btnPrimary.Enabled = true;
                _btnDelete.Enabled = true;
                _txtContent.ReadOnly = true;
                _cboEditCategory.Enabled = false;
                _txtSource.ReadOnly = true;
                break;

            case EditorMode.New:
                _lblMode.Text = "新增记忆";
                _lblHint.Text = "填写后保存，自动写入 MEMORY.md 并重建 AGENTS.md 摘要";
                _btnPrimary.Text = "保存";
                _btnPrimary.Enabled = true;
                _btnDelete.Enabled = false;
                _txtContent.ReadOnly = false;
                _cboEditCategory.Enabled = true;
                _txtSource.ReadOnly = false;
                break;

            case EditorMode.Edit:
                _lblMode.Text = "编辑条目 " + (_current?.Id ?? "");
                _lblHint.Text = "ID 与记录时间保持不变；Ctrl+Enter 保存";
                _btnPrimary.Text = "保存";
                _btnPrimary.Enabled = true;
                _btnDelete.Enabled = true;
                _txtContent.ReadOnly = false;
                _cboEditCategory.Enabled = true;
                _txtSource.ReadOnly = false;
                break;
        }
    }

    private void SaveEditor()
    {
        if (_mode == EditorMode.View)
        {
            BeginEditSelected();
            return;
        }

        if (_mode == EditorMode.None)
        {
            BeginNew();
            return;
        }

        var category = ParseCategoryName(_cboEditCategory.Text);
        var content = _txtContent.Text.Trim();
        var source = _txtSource.Text.Trim();

        if (content.Length == 0)
        {
            MessageBox.Show("内容不能为空。", "MemoryDeck", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _txtContent.Focus();
            return;
        }

        WriteResult result;
        string? newId = null;

        if (_mode == EditorMode.New)
        {
            result = _repo.Add(category, content, source, out var id);
            newId = id;
        }
        else
        {
            result = _repo.Update(new MemoryEntry
            {
                Id = _txtId.Text.Trim(),
                Category = category,
                Content = content,
                Source = source,
            });
            newId = _txtId.Text.Trim();
        }

        if (!result.Ok)
        {
            MessageBox.Show(result.Message, "MemoryDeck", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        ReloadAll(selectFirst: false);
        if (newId is not null) SelectRowById(newId);

        var message = result.Message;
        if (result.AgentsWarning is not null) message += "　（" + result.AgentsWarning + "）";
        SetStatus(message, warning: result.AgentsWarning is not null);
    }

    private void DeleteSelected()
    {
        var entry = CurrentSelectedEntry() ?? _current;
        if (entry is null) return;

        var answer = MessageBox.Show(
            $"确定删除 {entry.Id} 吗？{Environment.NewLine}{Environment.NewLine}{entry.Content}{Environment.NewLine}{Environment.NewLine}删除前会自动备份 MEMORY.md。",
            "删除记忆",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);

        if (answer != DialogResult.Yes) return;

        var result = _repo.Remove(entry);
        if (!result.Ok)
        {
            MessageBox.Show(result.Message, "MemoryDeck", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _current = null;
        ReloadAll(selectFirst: true);
        BeginNew();
        SetStatus(result.Message);
    }

    // ================================================================ 外部变更监视

    private void WatchForExternalChanges()
    {
        if (!Visible || WindowState == FormWindowState.Minimized) return;
        if (_mode is EditorMode.New or EditorMode.Edit) return;

        var fingerprint = "";
        try
        {
            var fresh = _repo.Load();
            fingerprint = fresh.Fingerprint;
        }
        catch
        {
            return;
        }

        if (fingerprint == _lastFingerprint) return;

        var selectedId = CurrentSelectedEntry()?.Id;
        ReloadAll(selectFirst: false);
        if (selectedId is not null) SelectRowById(selectedId);
        SetStatus("检测到外部修改，已自动重新加载。");
    }

    // ================================================================ 命令

    private void ChangeDataDirectory()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择包含 MEMORY.md / AGENTS.md 的数据目录",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_config.DataDirectory) ? _config.DataDirectory : Environment.CurrentDirectory,
            ShowNewFolderButton = true,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        _config = new StorageConfig { DataDirectory = dialog.SelectedPath };
        _config.Save();
        _repo = new MemoryRepository(_config);

        ReloadAll(selectFirst: true);
        SetStatus("已切换数据目录：" + _config.DataDirectory);
    }

    private void DoBackup()
    {
        try
        {
            var path = _repo.AutoBackup();
            SetStatus(path.Length == 0 ? "没有可备份的 MEMORY.md。" : "已备份到：" + path);
        }
        catch (Exception ex)
        {
            SetStatus("备份失败：" + ex.Message, warning: true);
        }
    }

    private void DoSyncAgents()
    {
        try
        {
            var ok = _repo.SyncAgentsSummary(_snapshot.Entries);
            SetStatus(ok ? "AGENTS.md 摘要已重建。" : "AGENTS.md 中未找到摘要标记区。", warning: !ok);
            ReloadAll(selectFirst: false);
        }
        catch (Exception ex)
        {
            SetStatus("同步失败：" + ex.Message, warning: true);
        }
    }

    private void OpenInExplorer(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
                return;
            }

            var parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
            {
                Process.Start(new ProcessStartInfo { FileName = parent, UseShellExecute = true });
                return;
            }

            SetStatus("路径不存在：" + path, warning: true);
        }
        catch (Exception ex)
        {
            SetStatus("打开失败：" + ex.Message, warning: true);
        }
    }

    private void OpenFileOrDefault(string path)
    {
        if (!File.Exists(path))
        {
            OpenInExplorer(path);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetStatus("打开失败：" + ex.Message, warning: true);
        }
    }

    private void ShowHelp()
    {
        const string text = """
            MemoryDeck 使用说明

            数据源
              · MEMORY.md 是唯一数据源，程序读写的就是这张 Markdown 表格。
              · AGENTS.md 的摘要区（两支 MEMORY-DECK:SUMMARY 标记之间）在每次写入后自动重建，
                这样 AI 在新会话里加载到的记忆摘要永远是最新的。
              · 每次写入前会把旧文件复制到 <数据目录>\.memory-deck\backups，最多保留 30 份。

            常用操作
              Ctrl+N        新增记忆（自动分配下一个 ID）
              Ctrl+E        编辑选中条目
              Ctrl+S        保存当前编辑
              Ctrl+F        跳到搜索框
              Ctrl+D        收起 / 展开下方详情区
              Delete        删除选中条目
              F5            重新加载文件
              双击表格行     编辑该条目
              Ctrl+Enter    在内容框里直接保存
              右键表格行     查看完整信息（来源 / 记录时间）、复制、编辑、删除

            关于界面
              表格只常显 ID / 类别 / 内容三列。
              来源与记录时间移到右键菜单「查看完整信息」，让列表更清爽。
              类别下拉是「建议」不是「限制」：预设类别之外可以自由输入新类别。
              表格为空时详情区会自动收起，把空间让给列表。

            同步行为
              程序每 1.5 秒检查一次文件指纹。你在编辑器或 AI 侧改过 MEMORY.md / AGENTS.md 后，
              界面会自动重新加载（正在编辑时不会打扰你）。

            命令行
              MemoryDeck.exe --data <目录>     指定数据目录
              MemoryDeck.exe --open            打开数据目录
              MemoryDeck.exe --selftest        运行数据层与界面自检
              MemoryDeck.exe --repair          规范化重写并校验数据无损
              MemoryDeck.exe --env             打印显示器 / DPI / 窗口尺寸诊断
            """;

        MessageBox.Show(text, "使用说明", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ShowAbout()
    {
        var version = typeof(MainForm).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        var runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;

        MessageBox.Show(
            $"MemoryDeck {version}{Environment.NewLine}" +
            $"记忆卡片管理器 ｜ 由 YG 的 AI 助手编写{Environment.NewLine}{Environment.NewLine}" +
            $"运行环境：{runtime}{Environment.NewLine}" +
            $"数据目录：{_config.DataDirectory}{Environment.NewLine}" +
            $"条目数量：{_snapshot.Entries.Count}{Environment.NewLine}{Environment.NewLine}" +
            "用 C# / WinForms 编写，单文件发布，Markdown 为唯一数据源。",
            "关于 MemoryDeck",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    // ================================================================ 键盘

    private void MainForm_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.N) { e.SuppressKeyPress = true; BeginNew(); }
        else if (e.Control && e.KeyCode == Keys.F) { e.SuppressKeyPress = true; _txtSearch.Focus(); _txtSearch.SelectAll(); }
        else if (e.Control && e.KeyCode == Keys.S) { e.SuppressKeyPress = true; SaveEditor(); }
        else if (e.Control && e.KeyCode == Keys.E) { e.SuppressKeyPress = true; BeginEditSelected(); }
        else if (e.Control && e.KeyCode == Keys.D) { e.SuppressKeyPress = true; SetEditorCollapsed(!_split.Panel2Collapsed); }
        else if (e.KeyCode == Keys.F5) { e.SuppressKeyPress = true; ReloadAll(selectFirst: true); }
    }

    private void Grid_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Delete) { e.SuppressKeyPress = true; DeleteSelected(); }
        else if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; BeginEditSelected(); }
    }

    private static bool Contains(string? source, string query) =>
        source is not null && source.Contains(query, StringComparison.OrdinalIgnoreCase);
}
