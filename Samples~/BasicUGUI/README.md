# Basic UGUI Schema

1. 首次使用 TextMeshPro 时，执行 `Window > TextMeshPro > Import TMP Essential Resources`。
2. 在 Project 中选中 `UIExample.json`。
3. 执行 `Assets > UI工具 > 根据选中的UISchema生成Prefab`。

只安装本包的项目会在 `Assets/GeneratedUI/Prefabs/UIExample.prefab` 得到可编辑的
Canvas、Image、TextMeshProUGUI 和 Button。示例使用纯色，不调用 AI，不需要项目 Sprite。
重新生成仅替换 `Generated` 子树，其他手工子节点会保留。

已接入自定义适配器的项目使用该适配器的默认输出目录和生成流程。
