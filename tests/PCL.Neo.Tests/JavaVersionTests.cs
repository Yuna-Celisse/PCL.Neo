using PCL.Neo.Core.Minecraft.Java;

namespace PCL.Neo.Tests;

/// <summary>
/// 用真实厂商的 <c>java -version</c> 原文做回归样本。
/// Java 9 之前 <c>1.8.0_381</c> 的主版本是 8，弄错会导致 Minecraft 版本要求判断全盘失效。
/// </summary>
public class JavaVersionTests
{
    /// <summary>真实厂商 java -version 原文样本：(输出, 主, 次, 更新, 构建)</summary>
    public static readonly object[][] RealWorldSamples =
    [
        // Temurin / Adoptium 17
        [
            "openjdk version \"17.0.9\" 2023-10-17\n" +
            "OpenJDK Runtime Environment Temurin-17.0.9+9 (build 17.0.9+9)\n" +
            "OpenJDK 64-Bit Server VM Temurin-17.0.9+9 (build 17.0.9+9, mixed mode, sharing)",
            17, 0, 9, 9
        ],
        // Oracle Java 8：传统命名，更新号在 _ 之后
        [
            "java version \"1.8.0_381\"\n" +
            "Java(TM) SE Runtime Environment (build 1.8.0_381-b09)\n" +
            "Java HotSpot(TM) 64-Bit Server VM (build 25.381-b09, mixed mode)",
            8, 0, 381, 9
        ],
        // Azul Zulu 8（Linux）
        [
            "openjdk version \"1.8.0_392\"\n" +
            "OpenJDK Runtime Environment (Zulu 8.74.0.17-CA-linux64) (build 1.8.0_392-b08)",
            8, 0, 392, 8
        ],
        // Oracle Java 21 LTS，build 在 + 之后
        [
            "openjdk version \"21.0.1\" 2023-10-17 LTS\n" +
            "OpenJDK Runtime Environment (build 21.0.1+12-LTS-29)",
            21, 0, 1, 12
        ],
        // Java 22：只有主版本
        [
            "openjdk version \"22\" 2024-03-19\n" +
            "OpenJDK Runtime Environment (build 22+36-2370)",
            22, 0, 0, 36
        ],
        // Debian 打包的内部构建
        [
            "openjdk version \"17.0.9-internal\" 2023-10-17\n" +
            "OpenJDK Runtime Environment (build 17.0.9+9-Debian-1)",
            17, 0, 9, 9
        ],
        // IBM Semeru / OpenJ9
        [
            "openjdk version \"17.0.9\" 2023-10-17\n" +
            "Eclipse Adoptium (build J9VM)",
            17, 0, 9, 0
        ],
        // 上古 Java 6
        [
            "java version \"1.6.0_45\"\n" +
            "Java(TM) SE Runtime Environment (build 1.6.0_45-b06)",
            6, 0, 45, 6
        ]
    ];

    [Theory]
    [MemberData(nameof(RealWorldSamples))]
    public void Parses_Real_World_Output(string output, int major, int minor, int patch, int build)
    {
        var version = JavaVersionParser.Parse(output);

        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(patch, version.Patch);
        Assert.Equal(build, version.Build);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(21)]
    public void Java8_Meets_Legacy_Requirements_But_Not_Modern(int minimum)
    {
        var java8 = JavaVersionParser.Parse("""java version "1.8.0_381" """);

        Assert.Equal(minimum <= 8, java8.MeetsMinimum(minimum));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bash: java: command not found")]
    [InlineData("version \"\"")]
    [InlineData("not a version at all")]
    public void Unparseable_Input_Returns_Null_Instead_Of_Throwing(string? input)
    {
        Assert.Null(JavaVersionParser.TryParse(input));
    }

    [Fact]
    public void Parse_Throws_For_Unparseable_Input()
    {
        Assert.Throws<FormatException>(() => JavaVersionParser.Parse("garbage"));
    }

    [Fact]
    public void Raw_Is_Preserved_For_Display()
    {
        var version = JavaVersionParser.Parse("""openjdk version "17.0.9" 2023-10-17""");
        Assert.Equal("17.0.9", version.Raw);
        Assert.Equal("17.0.9", version.ToString());
    }
}
