using System.Text;
using PCL.Neo.Core.Configuration;

namespace PCL.Neo.Tests;

public class IniDocumentTests
{
    [Fact]
    public void RoundTrip_Preserves_Comments_Blank_Lines_And_Order()
    {
        const string original =
            "; 用户手写的前言注释\n" +
            "[main]\n" +
            "Name=测试实例\n" +
            "\n" +
            "# 中间注释\n" +
            "JavaPath=C:\\Games\\java\\bin\\java.exe\n" +
            "[other]\n" +
            "Unknown.Key=保留我\n";

        var doc = IniDocument.Parse(original);
        var result = doc.ToText();

        Assert.Equal(original, result);
    }

    [Fact]
    public void Keys_Are_Case_Sensitive()
    {
        // Linux 文件系统大小写敏感；若按 Windows 习惯做不敏感比较，
        // 同一份配置在两个平台会解析出不同结果。
        var doc = IniDocument.Parse("[main]\nFoo=1\nFOO=2\n");

        Assert.True(doc.TryGetValue("main", "Foo", out var lower));
        Assert.Equal("1", lower);
        Assert.True(doc.TryGetValue("main", "FOO", out var upper));
        Assert.Equal("2", upper);
    }

    [Theory]
    [InlineData(@"C:\Games\!DOCS\Minecraft")]          // 大量反斜杠
    [InlineData("值=带等号")]                            // 值里含 =
    [InlineData("多行\n第二行")]                          // 值里含换行
    [InlineData("首尾空格 ")]
    [InlineData("中文与Emoji🎮")]
    public void Values_RoundTrip_Losslessly(string value)
    {
        var doc = new IniDocument();
        doc.SetValue("main", "K", value);

        var reparsed = IniDocument.Parse(doc.ToText());

        Assert.True(reparsed.TryGetValue("main", "K", out var read));
        Assert.Equal(value, read);
    }

    [Fact]
    public void SetValue_Creates_Missing_Section_At_End()
    {
        var doc = IniDocument.Parse("[main]\nA=1\n");
        doc.SetValue("instances", "B", "2");

        var text = doc.ToText();
        Assert.Contains("[instances]", text);
        Assert.True(IniDocument.Parse(text).TryGetValue("instances", "B", out var v));
        Assert.Equal("2", v);
    }

    [Fact]
    public void Unparseable_Lines_Are_Kept_As_Comments()
    {
        var doc = IniDocument.Parse("[main]\n这行没有等号\nA=1\n");
        Assert.Contains("这行没有等号", doc.ToText());
    }
}

public class SettingsStoreTests
{
    [Fact]
    public void Save_And_Load_RoundTrips_Typed_Values()
    {
        using var temp = TempDirectory.Create();
        var file = temp.PathOf("settings.ini");

        var store = SettingsStore.Load(file);
        store.SetString("LaunchVersion", "1.20.1");
        store.SetString("中文键名", "含 空格 与 = 号");
        store.SetInt("MaxMemory", 4096);
        store.SetBool("AutoLogin", true);
        Assert.True(store.Save());
        Assert.False(store.Save(), "无改动时不应重复写盘");

        var reloaded = SettingsStore.Load(file);
        Assert.Equal("1.20.1", reloaded.GetString("LaunchVersion"));
        Assert.Equal("含 空格 与 = 号", reloaded.GetString("中文键名"));
        Assert.Equal(4096, reloaded.GetInt("MaxMemory"));
        Assert.True(reloaded.GetBool("AutoLogin"));
    }

    [Fact]
    public void Missing_Keys_Fall_Back()
    {
        using var temp = TempDirectory.Create();
        var store = SettingsStore.Load(temp.PathOf("empty.ini"));

        Assert.Equal("默认", store.GetString("不存在", "默认"));
        Assert.Equal(7, store.GetInt("不存在", 7));
        Assert.True(store.GetBool("不存在", true));
        Assert.False(store.GetBool("不存在"));
    }

    [Fact]
    public void Corrupt_Integer_Value_Falls_Back_Instead_Of_Throwing()
    {
        using var temp = TempDirectory.Create();
        var file = temp.PathOf("settings.ini");
        File.WriteAllText(file, "[main]\nMaxMemory=不是数字\n", new UTF8Encoding(false));

        Assert.Equal(1024, SettingsStore.Load(file).GetInt("MaxMemory", 1024));
    }

    [Fact]
    public void Save_Leaves_No_Temp_File_And_Writes_Without_Bom()
    {
        using var temp = TempDirectory.Create();
        var file = temp.PathOf("settings.ini");

        var store = SettingsStore.Load(file);
        store.SetInt("X", 1);
        store.Save();

        Assert.True(File.Exists(file));
        Assert.False(File.Exists(file + ".tmp"), "原子写之后不应残留临时文件");
        Assert.NotEqual(0xEF, File.ReadAllBytes(file)[0]);
    }

    [Fact]
    public void Concurrent_Saves_Never_Produce_Corrupt_File()
    {
        // 启动器可能在游戏运行期间被多个后台任务写配置，
        // 半截配置文件会让用户连实例列表都打不开，因此必须原子。
        using var temp = TempDirectory.Create();
        var file = temp.PathOf("settings.ini");

        var store = SettingsStore.Load(file);
        Parallel.For(0, 40, i =>
        {
            store.SetInt($"Key{i}", i);
            store.Save();
        });

        var doc = IniDocument.Parse(File.ReadAllText(file));
        var keys = doc.Keys("main").ToList();

        Assert.Equal(40, keys.Count(k => k.StartsWith("Key", StringComparison.Ordinal)));
        Assert.All(keys, k => Assert.NotEqual("", k));
    }

    [Fact]
    public void Migrations_Apply_In_Order_And_Only_Once()
    {
        using var temp = TempDirectory.Create();
        var file = temp.PathOf("settings.ini");
        File.WriteAllText(file, "[meta]\nVersion=1\n[main]\nOldName=x\n", new UTF8Encoding(false));

        var applied = new List<string>();
        var migrations = new List<ConfigMigration>
        {
            new(2, doc =>
            {
                applied.Add("1to2");
                doc.SetValue("main", "NewName", doc.TryGetValue("main", "OldName", out var v) ? v : "");
                doc.Remove("main", "OldName");
            }),
            new(3, doc =>
            {
                applied.Add("2to3");
                doc.SetValue("main", "Level", "3");
            })
        };

        var store = SettingsStore.Load(file, migrations: migrations);
        Assert.Equal(["1to2", "2to3"], applied);
        Assert.Equal("x", store.GetString("NewName"));
        Assert.Equal("3", store.GetString("Level"));
        Assert.Equal(3, store.Version);

        // 再加载一次：已到位的版本不应重复执行迁移
        applied.Clear();
        SettingsStore.Load(file, migrations: migrations);
        Assert.Empty(applied);
    }

    [Fact]
    public void SettingChanged_Fires_Only_On_Real_Changes()
    {
        using var temp = TempDirectory.Create();
        var store = SettingsStore.Load(temp.PathOf("settings.ini"));
        var fired = new List<string>();
        store.SettingChanged += k => fired.Add(k);

        store.SetString("A", "1");
        store.SetString("A", "1");
        store.SetString("A", "2");

        Assert.Equal(["A", "A"], fired);
    }
}
