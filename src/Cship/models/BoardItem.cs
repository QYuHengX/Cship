using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Cship.Models;

public enum BoardItemType
{
    Apps,
    Files,
    Folders,
}

/// <summary>
/// 收纳板单项目（步骤 2.2）：Name/Path/Type/IconPath/SizeBytes/Mtime/IsRunning/Row/Col。
/// Row/Col 为网格单元格（-1=未分配），由 BoardWindow 布局时写入、IconGridPanel 读取。
/// </summary>
public sealed class BoardItem : INotifyPropertyChanged
{
    string _name = "";
    string _path = "";
    string _targetPath = "";
    BoardItemType _type;
    string _iconPath = "";
    long? _sizeBytes;
    DateTime _mtime;
    bool _isRunning;
    int _row = -1;
    int _col = -1;

    public string Name { get => _name; set => Set(ref _name, value); }
    public string Path { get => _path; set => Set(ref _path, value); }
    /// <summary>.lnk 解析出的目标路径（非快捷方式为空）——类型判定与指示灯检测用。</summary>
    public string TargetPath { get => _targetPath; set => Set(ref _targetPath, value); }
    public BoardItemType Type { get => _type; set => Set(ref _type, value); }
    public string IconPath { get => _iconPath; set => Set(ref _iconPath, value); }
    /// <summary>文件字节数；文件夹为 null（大小排序时排最后）。</summary>
    public long? SizeBytes { get => _sizeBytes; set => Set(ref _sizeBytes, value); }
    public DateTime Mtime { get => _mtime; set => Set(ref _mtime, value); }
    /// <summary>正在运行（指示灯）。</summary>
    public bool IsRunning { get => _isRunning; set => Set(ref _isRunning, value); }
    public int Row { get => _row; set => Set(ref _row, value); }
    public int Col { get => _col; set => Set(ref _col, value); }

    int _iconVersion;
    /// <summary>
    /// 图标就绪版本（07 §4 渐次回填）：后台提取完成后自增。图标缓存**路径不变**（键=path+mtime 哈希），
    /// 故不能只靠 <see cref="IconPath"/> 变化触发重载——IconItem 同时比较本值，就绪即重新解码。
    /// </summary>
    public int IconVersion { get => _iconVersion; set => Set(ref _iconVersion, value); }
    /// <summary>仅收纳板存在的"快捷方式"（源文件不在桌面；2026-08-29 批次六）。</summary>
    public bool IsVirtual { get; set; }

    /// <summary>一次性入场标志（清单三·任务12）：刷新重建队列置位、IconItem.DataContextChanged
    /// 消费（消费后即清）触发果冻入场；不进 PropertyChanged/不持久化，普通属性。</summary>
    public bool JellyPending { get; set; }

    /// <summary>显示名：快捷方式文件（.lnk/.url）不显示扩展名（2026-08-29 批次六，仅显示层——
    /// iconOrder/重命名/移除名单仍用真实 Name）。</summary>
    public string DisplayName
    {
        get
        {
            var ext = System.IO.Path.GetExtension(_name);
            if (ext is not null
                && (string.Equals(ext, ".lnk", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(ext, ".url", StringComparison.OrdinalIgnoreCase)))
                return _name[..^ext.Length];
            return _name;
        }
    }

    /// <summary>悬停提示名（2026-10-04 错误修复④）：完整名字；应用程序不显示后缀，文件保留扩展名，
    /// 文件夹原样（名字里带点也不误删）。</summary>
    public string HoverName
    {
        get
        {
            if (_type != BoardItemType.Apps) return _name;
            var ext = System.IO.Path.GetExtension(_name);
            return string.IsNullOrEmpty(ext) ? _name : _name[..^ext.Length];
        }
    }

    /// <summary>扫描差量同步：把新扫描结果合入既有实例（保持引用不变，选中/容器不重建）。</summary>
    public void UpdateFrom(BoardItem fresh)
    {
        TargetPath = fresh.TargetPath;
        Type = fresh.Type;
        IconPath = fresh.IconPath;
        SizeBytes = fresh.SizeBytes;
        Mtime = fresh.Mtime;
    }

    void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
