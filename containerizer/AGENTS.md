遵循 [../AGENTS.md](../AGENTS.md)，首先阅读双语 README、[SKILL.md](SKILL.md) 与 [docs/implementation.zh-Hans.md](docs/implementation.zh-Hans.md)。

- 制作端 `src` 使用 Core Profile、ConnectionSettings 与仓库 `.shared` 变量流程；现场 `executor/src` 只消费 JSON 协议及已生成资产，不引用 Core/YamlDotNet，也不解析 `.container`。
- `.shared/Containerization.props` 直接链接模型、文件及进程辅助源码，不生成共享 DLL，不引用 packager/migrator 的可执行项目。交接仅通过它们的产物和配套脚本。
- 新文本 CRLF、代码 Tab，Shell LF；资源通过 ResXFileCodeGenerator 更新，英文和中文同步。不得修改原有用户样例来迎合实现。
- `upgrade`、`prepare` 使用唯一交付包位置参数，禁止重新引入 `--bundle`。应用身份不含 tag。基础设施或 bootstrap 变化不能由应用升级隐式接受。
- 维护、失败和升迁尝试先持久化；失败不自动启动旧版本、不自动重跑 SQL。恢复沿用原事务，显式重试仅允许当前事务的失败/中断版本。
- 卸载默认保留持久资产；purge 校验实际归属、链接和挂载边界，最后删除状态。应用锁在可删除资产之外；主机锁与应用名使用独立文件名。
- 构建/测试只用本工具解决方案和测试项目。镜像制作及原生构建使用专属隔离容器；不能为验证运行现场安装、修改全局工具、服务或真实数据库。
- Cake default/test 不发布，build/compile 仅本地制包，pack 推送 NuGet。支持矩阵必须区分实现、单元测试、交叉编译与真实目标验收；未验证项不能列作受支持组合。
- 成员可见性由生产调用决定，不为测试将 private 扩大为 internal/public，不增加仅供测试访问的入口。文档只保留当前行为与实现契约，不记录任务过程。
