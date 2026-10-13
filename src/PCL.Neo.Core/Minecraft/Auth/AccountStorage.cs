using System.Text.Json;
using PCL.Neo.Core.Configuration;

namespace PCL.Neo.Core.Minecraft.Auth;

/// <summary>
/// 跨平台账户存储与持久化管理器。
/// 与 SettingsStore 协同，保持轻量无第三方库依赖。
/// </summary>
public sealed class AccountStorage(SettingsStore settings)
{
    private const string AccountsSection = "accounts";
    private const string ActiveAccountKey = "ActiveAccountUuid";
    private readonly SettingsStore _settings = settings ?? throw new ArgumentNullException(nameof(settings));

    public string? ActiveAccountUuid
    {
        get => _settings.GetString(ActiveAccountKey, "", AccountsSection);
        set
        {
            if (string.IsNullOrEmpty(value)) _settings.Remove(ActiveAccountKey, AccountsSection);
            else _settings.SetString(ActiveAccountKey, value, AccountsSection);
            _settings.Save();
        }
    }

    public void SaveAccount(MinecraftAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        var json = JsonSerializer.Serialize(account);
        _settings.SetString(account.Uuid, json, AccountsSection);
        if (string.IsNullOrEmpty(ActiveAccountUuid))
        {
            ActiveAccountUuid = account.Uuid;
        }
        else
        {
            _settings.Save();
        }
    }

    public IReadOnlyList<MinecraftAccount> LoadAllAccounts()
    {
        var list = new List<MinecraftAccount>();
        // 通过读取原始配置中的 keys
        return list;
    }
}
