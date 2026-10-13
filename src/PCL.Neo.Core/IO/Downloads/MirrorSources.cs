namespace PCL.Neo.Core.IO.Downloads;

/// <summary>
/// 社区主流的 Minecraft 国内镜像源规则生成器。
/// 支持 BMCLAPI 与 MCBBS 镜像源，对官方资源做无缝重定向。
/// </summary>
public static class MirrorSources
{
    public const string BmclapiHost = "https://bmclapi2.bangbang93.com";
    public const string McbbsHost = "https://download.mcbbs.net";

    /// <summary>
    /// 构建标准 BMCLAPI 镜像加速规则集合。
    /// 涵盖版本元数据、库依赖与资源对象。
    /// </summary>
    public static IReadOnlyList<MirrorRule> CreateBmclapiRules() =>
    [
        new("https://piston-meta.mojang.com", BmclapiHost),
        new("https://launchermeta.mojang.com", BmclapiHost),
        new("https://piston-data.mojang.com", BmclapiHost),
        new("https://libraries.minecraft.net", BmclapiHost + "/libraries"),
        new("https://resources.download.minecraft.net", BmclapiHost + "/assets")
    ];

    /// <summary>
    /// 构建 MCBBS 镜像加速规则集合。
    /// </summary>
    public static IReadOnlyList<MirrorRule> CreateMcbbsRules() =>
    [
        new("https://piston-meta.mojang.com", McbbsHost),
        new("https://launchermeta.mojang.com", McbbsHost),
        new("https://piston-data.mojang.com", McbbsHost),
        new("https://libraries.minecraft.net", McbbsHost + "/libraries"),
        new("https://resources.download.minecraft.net", McbbsHost + "/assets")
    ];
}
