using System.Collections.Generic;

namespace Cship.Models;

/// <summary>
/// 分类页单项目（步骤 04 · 2026-09-25 改版，state.categories 结构见 00 §8）：
/// 内置 <c>{"id":"apps|files|folders","builtin":true,"name":重命名覆盖(可选),"items":[显式拖入成员]}</c>
/// （默认按 BoardItem.Type 过滤∪显式成员；"移除"=catShow* 子开关关闭，可经设置找回）；
/// 自定义 <c>{"id":"cat_guid","name":"分类N","builtin":false,"items":[文件名]}</c>
/// （结构保留兼容，本版 UI 无创建入口）。"全部"为固定首项，不持久化（CategoryTabBar 内置）。
/// </summary>
public sealed class CategoryModel
{
    public string Id { get; set; } = "";
    /// <summary>显示名；内置项空/等于 id 时渲染层走 I18n（cat.apps 等），非空=用户重命名覆盖。</summary>
    public string Name { get; set; } = "";
    public bool Builtin { get; set; }
    /// <summary>内置分类的类型键（apps|files|folders，与 BoardItemType 对应）；自定义项为空。</summary>
    public string Type { get; set; } = "";
    /// <summary>显式成员（文件名列表，与 iconOrder/removedItems 同口径；拖入加入，内置/自定义通用）。</summary>
    public List<string> Items { get; } = new();

    public static CategoryModel CreateBuiltin(string id)
        => new() { Id = id, Name = "", Builtin = true, Type = id };

    public static CategoryModel CreateCustom(string name)
        => new() { Id = "cat_" + System.Guid.NewGuid().ToString("N")[..8], Name = name, Builtin = false };
}
