# 实现细节

## 本地搜索与源链接

本地模式由 Core Searcher 处理，工具不再维护通配递归算法。搜索结果保留逻辑名称，读取实际目标。选中目录链接作为载荷根时允许展开，内部目录链接跳过，文件链接按原名称读取目标内容。递归模式不穿过目录链接匹配后续段。选中链接悬空或循环会在输出写入前失败；目标路径校验继续执行。

链接 INI 和 .deploy 的相对引用以逻辑配置目录为基准。单模式结果按逻辑相对路径执行 Ordinal 排序，多参数顺序不变。参见 [Core 本地搜索](../../../framework/Zongsoft.Core/docs/searcher.zh-Hans.md)和[任务清单](../LOCAL-SEARCHER-TASKS.md)。

`Searcher.Search` 通过 `Searcher.Target` 选择文件、目录或两者（默认 Both）；`Match.Origin` 提供逻辑固定目录前缀，用于计算相对输出路径。
