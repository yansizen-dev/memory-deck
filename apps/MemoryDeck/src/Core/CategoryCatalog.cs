namespace MemoryDeck.Core;

/// <summary>一个预设类别的定义。</summary>
public sealed record CategoryDefinition(string Name, string Description);

/// <summary>
/// 类别目录。这里定义的是**给出建议**，不是限制：
/// 界面上的类别框始终可以自由输入，数据里出现的任何类别都会原样保留。
/// 预设只负责两件事：让新用户不用自己想分类名；让下拉里始终有可选项。
/// </summary>
public static class CategoryCatalog
{
    /// <summary>预设类别。顺序即下拉显示顺序，身份类在前、工程类居中、知识类在后。</summary>
    public static readonly IReadOnlyList<CategoryDefinition> Presets = new[]
    {
        new CategoryDefinition("身份", "我是谁：背景、角色、当前状态"),
        new CategoryDefinition("偏好", "我喜欢怎样被对待、怎样协作"),
        new CategoryDefinition("项目", "在做的事、里程碑、决定"),
        new CategoryDefinition("技术栈", "在用的语言、工具、框架"),
        new CategoryDefinition("目标", "想达成什么、下一步计划"),
        new CategoryDefinition("约束", "不能做什么、硬性边界"),
        new CategoryDefinition("技能", "会什么、正在学什么"),
        new CategoryDefinition("连接", "人脉、协作方、联系方式线索"),
        new CategoryDefinition("资料", "外部材料的位置与要点"),
        new CategoryDefinition("随手记", "暂时不知道归哪类，先记下来"),
    };

    /// <summary>筛选下拉里的「全部」项文本。</summary>
    public const string AllLabel = "全部";

    public static IReadOnlyList<string> PresetNames { get; } = Presets.Select(p => p.Name).ToArray();

    /// <summary>取某个类别的说明；不是预设就返回空串。</summary>
    public static string DescribeOf(string name)
    {
        foreach (var preset in Presets)
        {
            if (string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase))
                return preset.Description;
        }

        return "";
    }

    /// <summary>
    /// 合并「预设类别」与「数据中实际出现过的类别」，保持预设在前、额外项在后。
    /// 这样既保证下拉永远有选项，又不会丢掉用户已存在的自定义类别。
    /// </summary>
    public static IReadOnlyList<string> MergeWith(IEnumerable<string> existing)
    {
        var result = new List<string>(PresetNames);
        var seen = new HashSet<string>(PresetNames, StringComparer.OrdinalIgnoreCase);

        foreach (var raw in existing)
        {
            var name = (raw ?? "").Trim();
            if (name.Length == 0) continue;
            if (seen.Add(name)) result.Add(name);
        }

        return result;
    }
}
