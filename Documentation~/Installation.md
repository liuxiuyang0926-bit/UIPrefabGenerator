# 安装与项目适配

工具名称为 `LxyGame.UIPrefabGenerator`。为兼容已有项目，UPM 包 ID 继续使用
`com.lxy.ui-effect-generator`，程序集与命名空间继续使用 `Lxy.UIEffectGenerator.Editor`。
使用下面的新 URL 更新同一个包即可，无需同时安装两个版本。

## 从 Git URL 安装

包内仅包含通用 Editor 代码，不需要复制 LxyDemo 的 `Assets`、业务包或项目适配器。
支持 Unity 2022.3；Package Manager 会解析本包声明的 UGUI 和 TextMeshPro 依赖。

1. 在目标工程打开 `Window > Package Manager`。
2. 点击 `+ > Add package from git URL`。
3. 输入存放本包的 Git URL，然后点击 `Add`。

独立仓库的根目录直接包含 `package.json` 和 `Editor/`。固定版本地址：

```text
https://github.com/liuxiuyang0926-bit/UIPrefabGenerator.git#v1.1.4
```

需要跟随最新发布提交时也可使用：

```text
https://github.com/liuxiuyang0926-bit/UIPrefabGenerator.git#main
```

旧的 `LxyProject.git?path=...` 地址不再用于分发本包。`path` 只指定包在仓库中的位置，
无法把 Git 下载限制到那个子目录；大工程仓库容易因下载时间长而中断。
独立仓库从包快照建立新历史，没有带入工程的 Assets 或旧提交。地址规则见
[Unity Git dependencies](https://docs.unity3d.com/2022.3/Documentation/Manual/upm-git.html)。
制作机需安装 Git 并加入 PATH；私有仓库使用该制作机已有的 Git 访问凭据。

## 首次使用

1. 首次使用 TextMeshPro 时，执行 `Window > TextMeshPro > Import TMP Essential Resources`。
   中文 UI 还需项目自己的 TMP 中文字体，不能依赖其他工程里的字体资源。
2. 在 `Tools > UI Tools > Generate Prefab From Design` 或
   `工具 > UI工具 > 根据效果图生成Prefab` 打开窗口。
3. 在“默认 TMP 字体”中选择当前项目的字体。中文 UI 应选择含所需中文字形的 TMP 字体；
   留空时使用 TMP Settings 的默认字体。这项选择按项目保存，不会继承其他工程的字体路径。
4. 选择效果图、Prefab 输出目录和当前项目的 Sprite 总目录，然后生成。

Package Manager 的 Samples 中提供 `Basic UGUI Schema`，导入后可选中
`UIExample.json`，执行 `Assets > UI工具 > 根据选中的UISchema生成Prefab`。
这个示例不调用 AI，也不依赖业务 Sprite，适合先验证安装。

高精度/轻量 AI 分析要求制作机能够执行 `codex --version`，并已完成 Codex CLI 登录。
默认高精度模式不依赖 UnityMCP。目标工程可以不是 Git 仓库；包调用 `codex exec` 时
继续使用 `--skip-git-repo-check` 和只读沙箱。Figma 来源还需要制作机已有的 Figma MCP 授权。
轻量和高精度模式的提示均自包含，不要求目标项目安装 LxyDemo 的 Skill 文件。

## 更新与字体兼容性

1.1.1 修复了 AI 拼错字体路径或复用其他工程 Schema 时因找不到字体而中止生成的问题。
原有有效字体保持不变；缺失字体和不兼容材质按当前项目资源回退并记录提示。
缺少中文字体仍需提供项目字体，工具不会复制其他工程的字体或伪装成同款字体。

请在目标工程使用上面的独立仓库地址更新 Git 包，并确认版本为 `1.1.4`。Git 依赖会记录解析到的提交，
仅推送远程不会自动替换已有的 PackageCache；可在 Package Manager 中重新添加相同 Git URL
请求更新，或将 URL 的 `#main` 替换为刚推送的确切提交号。不要直接修改 `Library/PackageCache`。
已分析成功、仅在 Prefab 构建时失败的 Schema 可以直接重建，无需再次调用 AI。

## 从本地安装

公司网络无法访问 Git 时，可分发本包的 `.tgz` 文件，在 Package Manager 中选择
`Add package from tarball` 安装，无需解压。每次更新分发新的版本包。
`UIPrefabGenerator-1.1.4.tgz` 包含跨工程 Sprite 匹配、误补文字底板和徽标透明留边修复；
升级后可先使用已有 UISchema 重建 Prefab，
无需重新调用 AI。若原 Schema 将纹理节点误标成 ColorFallback，需复核该节点或重新分析。

在 Unity Package Manager 中选择 `Add package from disk`，指向本包的 `package.json`。
也可将整个目录复制到目标项目的 `Packages/LxyGame`，并在项目的 `Packages/manifest.json` 中加入：

```json
"com.lxy.ui-effect-generator": "file:LxyGame/LxyGame.UIPrefabGenerator"
```

## 通用项目默认行为

没有注册项目适配器时，生成器自动使用 `Generic UGUI`：

- Prefab 输出到 `Assets/GeneratedUI/Prefabs`；
- UISchema 输出到 `Assets/Editor/UIEffectGenerator/Schemas`；
- 参考图输出到 `Assets/Editor/UIEffectGenerator/References`；
- Figma 裁切 Sprite 输出到 `Assets/GeneratedUI/FigmaSprites`；
- Sprite 默认从 `Assets` 及全部子目录扫描；
- Prefab 根包含 `RectTransform`、`Canvas`、`GraphicRaycaster` 和
  `CanvasGroup`；
- 不生成业务脚本，不挂载项目专属 Binder；
- 整个框架配置区域不显示：Prefab 适配器、脚本类型、C# 命名空间、逻辑类名、
  脚本目录、UI 层级及通用适配器提示均隐藏。

窗口可设置 Prefab 输出目录和资源总目录，其他默认目录可由项目适配器提供。
资源总目录应尽量选择实际 UI Sprite 的
共同父目录，以减少扫描时间并提高候选质量。

## 接入项目自己的 UI 框架

项目可以在自己的 Editor 程序集中实现
`Lxy.UIEffectGenerator.Editor.IUIEffectProjectAdapter`，并在
`InitializeOnLoad` 初始化时注册：

```csharp
using Lxy.UIEffectGenerator.Editor;
using UnityEditor;

[InitializeOnLoad]
internal static class MyUIEffectAdapterRegistration
{
    private static readonly IUIEffectProjectAdapter Adapter =
        new MyUIEffectProjectAdapter();

    static MyUIEffectAdapterRegistration()
    {
        UIEffectProjectAdapterRegistry.Register(Adapter, 100);
    }
}
```

适配器只负责：

1. 创建或加载项目标准 Prefab 外壳；
2. 在替换旧 `Generated` 前移除项目 Binder 中指向旧节点的引用；
3. 新树创建后刷新 Binder、脚本元数据或项目层级配置。

效果图分析、UISchema 校验、Sprite 匹配、组件创建和 Prefab 内容校验仍由
独立包负责。适配器不应重新实现或二次调用视觉匹配。

LxyDemo 的参考实现位于：

`Packages/LxyGame/LxyGame.Logic/Editor/UI/UI/UIEffectLxyProjectAdapter.cs`

它继续调用原有 `CSharpUIGenerator`，因此 LxyDemo 中的 ObjectBinder、
UICodeBinder、C#/Lua 配置和默认目录保持不变。
该文件属于 Lxy 的业务包，不随生成器独立包导出。只有适配器声明支持脚本生成或
层级选择时，窗口才绘制相应配置；不通过工程名、机器路径或业务程序集反射判断。

## Editor API

```csharp
using Lxy.UIEffectGenerator.Editor;

UIEffectPrefabGenerationResult result =
    UIEffectPrefabBuilder.GenerateFromSchemaPath(
        "Assets/Editor/UIEffectGenerator/Schemas/UIExample.json",
        new UIEffectPrefabGenerationOptions
        {
            panelId = "UIExample",
            prefabFolder = "Assets/GeneratedUI/Prefabs",
            resourceMatchMode =
                UIEffectResourceMatchMode.VisualSimilarity,
            resourceSearchRoots = new[]
            {
                "Assets/Game/UI/Sprites",
            },
        },
        true);
```

## 从 LxyDemo 导出独立仓库内容

在 LxyDemo 根目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Export-UIPrefabGenerator.ps1 -CreateTarball
```

输出到 `Build/UPMPackages/UIPrefabGenerator-1.1.4/`，并创建同目录的 `.tgz` 离线包。
省略 `-CreateTarball` 时只导出文件夹。也可传入
`-Destination <空目录>`。脚本保留 `.meta`，只复制包文件，并校验依赖和 Editor 程序集边界；
不会覆盖已有非空目录，也不会包含 Lxy 项目适配器、业务代码、游戏资源或账号配置。
命令中的执行策略仅作用于本次 PowerShell 进程，不修改系统设置。

将输出目录的**内容**放到独立 Git 仓库根目录，提交并推送后即可使用该仓库的 `.git` URL。
后续版本继续从同一包目录导出，保持包名和已有 `.meta` GUID 不变。
导出脚本只准备文件，不创建远程仓库或自动推送。

## 发布检查

独立包目录本身就是标准 UPM 包。发布前至少验证：

- 新建的普通 Unity 2022.3 工程只安装此包即可编译；
- 菜单能打开，且不显示项目框架配置区域；
- 纯色 UISchema 能生成 Prefab，根节点没有 Missing Script；
- 选择项目 Sprite 根后，视觉模式能正确匹配普通和九宫格 Sprite；
- 重新生成只替换 `Generated`，手工兄弟节点保持不变。
