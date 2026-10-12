# 许可与合规矩阵

本仓库的许可**不是单一的**。下表是唯一权威来源，新增项目时必须同步更新。

| 项目 / 目录 | 许可证 | 依据 |
| --- | --- | --- |
| `src/PCL.Neo/` | 《PCL 分发有限许可》+《PCL 存储库合理使用指南》 | [README 协议声明](../README.md#协议声明)，与主线一致 |
| `src/PCL.Neo.Desktop/` | 同上 | 同上 |
| `src/PCL.Neo.UI/` | MIT | 根目录 `LICENSE` |
| `src/PCL.Neo.Core/` | **Apache-2.0（预留）** | 见下文「移植政策」 |
| `tests/` | MIT | 根目录 `LICENSE` |
| `docs/`、`assets/` | MIT | 根目录 `LICENSE` |

## 为什么 Core 要单独用 Apache-2.0

PCL-CE 的 `PCL.Core` 采用 **Apache License 2.0**，而本仓库其余部分采用 **MIT**。
把 Apache-2.0 的代码放进以 MIT 授权的项目里，等同于把它**再授权**为 MIT——这是
Apache-2.0 明确不允许的。

因此规定：

1. **所有自 `PCL.Core` 移植的代码只能进入 `src/PCL.Neo.Core/`**，该项目按 Apache-2.0 授权。
2. 移植的每个文件顶部保留 Apache-2.0 版权头，并在根目录 [`NOTICE`](../NOTICE) 登记来源与改动。
3. `PCL.Neo.UI`（MIT）**不得**引用 `PCL.Neo.Core` 中的 Apache 代码作为其许可基础；
   引用本身合法，但组合后的分发必须分别声明两份许可。
4. 若某模块确实需要 Apache 代码与 MIT 代码同处一个程序集，则**整个程序集升为 Apache-2.0**，
   不允许反向操作。

## 不可移植的部分

`PCL-CE` 的 `Plain Craft Launcher 2/`（含 `ModLaunch.cs`、`ModDownload.cs`、`ModComp.cs`
等约 2.2 万行启动/下载/组件逻辑）使用《PCL 分发有限许可》，**不得复制源码**。
这些功能在本项目中一律按「阅读其接口与行为、独立重新实现」处理。

## 《PCL 存储库合理使用指南》重度使用义务与落实位置

PCL.Neo 属于指南定义的**重度使用**（重新实现 PCL 的实质功能），义务与落实：

| 条款要求 | 落实位置 | 状态 |
| --- | --- | --- |
| 明示为第三方独立二次创作 | 关于页首段（`Views/AboutView.axaml`） | ✅ |
| 关于页首位署名龙腾猫跃 + 赞助链接 | 关于页「首位」区块，链接 `meloong.com/afd/a/LTCat` | ✅ |
| 公开源代码 | 本仓库 | ✅ |
| 继续使用本指南 | 本文件 + `NOTICE` | ✅ |
| 命名须以 Plain Craft Launcher (PCL) 开头并加区分后缀 | 项目名 `PCL.Neo` | ⚠️ 由社区授权覆盖 |
| 实现与 `McLaunchPrecheck` 一致的正版购买与赞助劝导 | 启动流程（M4/M5 里程碑） | ⏳ 待实现，**上线前必须完成** |
| Windows 上界面主色相须为蓝色 | 当前默认 accent 为 `#fa9545`（橙） | ⚠️ 已知偏离，由维护者决定 |

> 最后两行是**发布闸门**：任何带启动功能的对外版本，都必须先补齐正版劝导，
> 并就 Windows 主色相问题作出明确决定（改默认色，或取得显式授权）。
