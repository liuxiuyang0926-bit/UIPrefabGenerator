# Lxy UIPrefabGenerator

面向 Unity 2022.3 的可移植 Editor 包，可以从本地效果图、Figma 节点或
UISchema 2.0 生成可编辑的 UGUI Prefab。Sprite 视觉匹配、九宫格、层级恢复、
运行时模板裁剪和高精度 Codex 两阶段分析都位于这个独立包内。

1.1.3 起，Package Manager 中显示为 `LxyGame.UIPrefabGenerator`，窗口标题为
“UI预制体生成器”。包 ID、程序集/API 标识和已有设置保持兼容，旧工程可直接更新。

## Git URL 安装

在 `Window > Package Manager > + > Add package from git URL` 中输入：

```text
https://github.com/liuxiuyang0926-bit/UIPrefabGenerator.git#v1.1.4
```

独立仓库根目录只包含本包，拥有独立的轻量 Git 历史。
旧的 `LxyProject.git?path=...` 地址需要获取整个工程仓库的 Git 数据，不再用于分发本工具。
公司网络无法使用 Git 时，使用 `UIPrefabGenerator-1.1.4.tgz`，通过
`Package Manager > + > Add package from tarball` 安装。
具体地址格式、发布步骤和首次使用说明见 [Installation](Documentation~/Installation.md)。

其他项目只安装本包即可使用通用 UGUI 生成流程，窗口隐藏整块脚本/框架配置。
LxyDemo 的适配器保留在业务包中，继续显示原有配置并处理 Canvas、Binder、C#/Lua 脚本。
Package Manager 还提供 `Basic UGUI Schema` 示例，可先验证无需 AI 的 Prefab 生成。

“Prefab 目录”和“资源总目录”均支持拖入或选择项目中的文件夹。Prefab 输出目录按项目记忆，
下方显示实际保存路径；清空选择后恢复项目默认目录，尚不存在的默认目录会在生成时自动创建。

## 依赖

- Unity UI (`com.unity.ugui`)
- TextMeshPro (`com.unity.textmeshpro`)
- Unity 内置图片编解码和 JSON 序列化模块（由 Package Manager 解析）
- 每台制作机单独安装并登录 Codex CLI

默认 `Generic UGUI` 适配器只创建标准 UGUI Prefab，不依赖 ObjectBinder、
XLua、YooAsset 或任何业务 UI 框架。生成器只拥有 Prefab 根节点下的
`Generated` 子树，重新生成会保留其他手工子节点。

生成节点按组件类型命名：Button 使用 `Bnt_`，Image 使用 `Img_`，Text 使用 `Txt_`，
例如 `Bnt_Confirm`、`Img_Background`、`Txt_Title`。已有同类前缀会先归一，避免重复添加；
按钮辅助文字和滚动视口也遵循此规则，Toggle / ScrollRect 等复合控件保留自身命名。
命名不改变 Schema 的 `binding` 策略。项目适配器可在 `AfterBuildGeneratedTree` 中读取
`options.GeneratedBindingObjects` 获取实际允许绑定的节点；Lxy 适配器已处理此过滤。

窗口的“默认 TMP 字体”用于选择当前项目字体，并按项目保存。有效的 Schema 字体覆盖仍保留；
缺失字体或不兼容材质会回退并显示提示，不再因此中断整个 Prefab。AI 不再填写未经证实的字体路径。
中文 UI 需要项目中包含相应字形的 TMP 字体；未导入 TMP 必需资源时会在 AI 开始前提示处理。

安装后通过以下任一菜单打开：

- `工具/UI工具/根据效果图生成Prefab`
- `Tools/UI Tools/Generate Prefab From Design`

安装、迁移和自定义项目适配器见
[Installation](Documentation~/Installation.md)。

高精度模式先量取结构，再逐节点检索完整资源库，并向第二阶段提供参考裁片、
候选原貌及按目标尺寸渲染的 UGUI 图。候选不再按路径截取全局前 100 项。
AI 指定的资源会经过本地验证；候选被拒绝后不会在创建组件时直接绑定第一项。

生成后可点击“打开还原检查报告与预览”，查看原图、包含文字的实际生成结果、
差异图及待复核节点。报告保存在 `Library/LxyUIEffectGenerator/Reports/<Panel>/`。
差异只检查生成范围内可比较的图像像素，文字单独检查，不把分数当作整图还原率。

已有 Schema 默认继续稳定复用。升级后要重新分析效果图，请关闭“同图复用现有 Schema”
并运行高精度生成。单次首次生成会调用两次 AI；仅重建 Prefab 或复用 Schema 不调用 AI。

扩展字段与验证方式见 [Fidelity](Documentation~/Fidelity.md)。

窗口顶部在生成过程中显示静态提示，结束后显示本次总耗时，不实时读秒，可展开最近 20 次总耗时记录。
总耗时包含本次 AI 分析、资源检索、本地匹配、Prefab 保存和生成检查。
复用 Schema 时不计历史 AI 分析时间。成功、失败、取消均分别记录，历史保存在项目的
`Library/LxyUIEffectGenerator/GenerationHistory.asset`，重新打开窗口仍可查看。

历史数据仍保存效果图准备、AI 结构分析、全库候选检索、候选对照图准备、
AI 资源与结构校正、结果校验以及 Unity 构建的分阶段耗时，供排查性能使用，界面只显示总耗时。
结构阶段只携带本阶段相关规则，两轮复用相同效果图附件；候选清单采用紧凑表格，
保留所有候选及评分、源尺寸、Border、颜色信息，对照图复用每个节点的渲染场景。
推理强度仍为 xhigh/high，匹配范围、候选数量、分辨率和验收阈值不变。
完整匹配结果仅在采样像素、遮挡、逐点背景、语义、候选顺序、评分选项及资源索引
完全一致时复用；带动态过滤器的检索继续完整计算。资源变更/重扫会清理缓存。

候选像素评分按独立候选并行计算，单个候选内部的浮点运算顺序和最终稳定排序不变。
Unity 渲染与资源访问仍在编辑器主线程执行。九宫格素材的原始 RGBA32 像素缓存
跨目标尺寸复用，上限 192 MiB；目标尺寸、Border、遮挡和背景评分仍逐节点计算。
第一轮 AI 等待期间按每次更新约 8 ms 的预算预计算这些原始像素。
取消、关闭窗口或资源版本变化会停止预计算；未完成部分由正常匹配流程继续计算。

排名列表（FirstPlace/SecondPlace/ThirdPlace）按运行时数据模板识别，匹配后只保留一份，
并维持原位置与内部层级；不同徽标的原始比例不会把同构名次项误判成固定面板。
独立 Field/Bar/Plate 底板不会仅因父图局部颜色相似而自动删除。艺术字候选保留透明轮廓，
使用匹配祖先的合成底色比较，MiddleLeft/MiddleRight 文本对齐可正确映射到 TMP。
