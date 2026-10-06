using System;

namespace Cship.Bootstrapper
{
    /// <summary>
    /// 下载源清单（2026-10-06 实测三源均可直连）。版本优先在线查询微软 8.0 线元数据，
    /// 查询失败时退回 FallbackVersion（发布时的最新补丁版）。
    /// </summary>
    internal static class KnownSources
    {
        /// <summary>兜底版本：构建日（2026-10-06）8.0 线最新补丁。</summary>
        public const string FallbackVersion = "8.0.31";

        /// <summary>8.0 线版本元数据（约 1.5 MB；只提取 latest-runtime 字段）。</summary>
        public const string MetaUrl = "https://builds.dotnet.microsoft.com/dotnet/release-metadata/8.0/releases.json";

        /// <summary>
        /// 两个压缩包的 URL 模板（{0}=版本 {1}=架构）。顺序即优先级，前一个源失败自动切下一个：
        /// 包 0 = 基础运行时（host\fxr + Microsoft.NETCore.App）；包 1 = WPF 框架（Microsoft.WindowsDesktop.App）。
        /// 两者叠加解压后构成完整的 DOTNET_ROOT 布局——官方没有单文件"桌面完整根"zip，必须两包合一。
        /// </summary>
        public static readonly string[][] Packages = new string[][]
        {
            new string[]
            {
                "https://builds.dotnet.microsoft.com/dotnet/Runtime/{0}/dotnet-runtime-{0}-win-{1}.zip",
                "https://dotnetcli.azureedge.net/dotnet/Runtime/{0}/dotnet-runtime-{0}-win-{1}.zip",
                "https://dotnetcli.blob.core.windows.net/dotnet/Runtime/{0}/dotnet-runtime-{0}-win-{1}.zip",
            },
            new string[]
            {
                "https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/{0}/windowsdesktop-runtime-{0}-win-{1}.zip",
                "https://dotnetcli.azureedge.net/dotnet/WindowsDesktop/{0}/windowsdesktop-runtime-{0}-win-{1}.zip",
                "https://dotnetcli.blob.core.windows.net/dotnet/WindowsDesktop/{0}/windowsdesktop-runtime-{0}-win-{1}.zip",
            },
        };
    }
}
