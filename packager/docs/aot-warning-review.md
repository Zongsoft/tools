# Native AOT warning review

The Rocky Linux 9 / glibc 2.34 publishes for `linux-x64` and `linux-arm64` completed with the existing package versions. Each publish reported 51 third-party IL warnings, including six IL3050 warnings. There were no IL warnings in the packager migration source. Publishing an ELF executable does not establish that every feature of a database driver works under AOT.

固定依赖版本的两个架构原生发布均成功，但发布成功不能代替功能验证。以下列出展开后的每条警告及其对应功能；未隐藏警告、未移除驱动，也未为失败路径提供托管运行器。实际数据库连接、建库、SQL 执行和失败处理的验收结果见 [升迁验证记录](migration-verification.md)。

日志由 `migrator/build/publish.sh` 保存到 `migrator/src/bin/aot/<rid>/publish.log`，ELF、原生依赖与符号记录保存在同一目录。下表中的功能范围来自调用方法和当前运行器入口；“没有配置”或“没有调用”仅描述当前代码，不能证明该驱动的其他入口支持 AOT。特别是脚本返回复杂数据时，驱动仍可能创建内部 reader，不能仅凭使用 ExecuteNonQuery 就排除所有复杂类型路径。

| Warning | Method | 对应功能与验证范围 |
| --- | --- | --- |
| IL2026 | `Microsoft.Data.SqlClient.Diagnostics.SqlDiagnosticListener.WriteConnectionCloseBefore(SqlConnection,String)` | 诊断事件载荷属性保留；运行器没有注册 DiagnosticListener 订阅者。连接及命令正常执行仍需实际验证。 |
| IL2026 | `Microsoft.Data.SqlClient.Diagnostics.SqlDiagnosticListener.WriteConnectionCloseError(Guid,Guid,SqlConnection,Exception,String)` | 诊断事件载荷属性保留；运行器没有注册 DiagnosticListener 订阅者。连接及命令正常执行仍需实际验证。 |
| IL2026 | `Microsoft.Data.SqlClient.Diagnostics.SqlDiagnosticListener.WriteConnectionCloseAfter(Guid,Guid,SqlConnection,String)` | 诊断事件载荷属性保留；运行器没有注册 DiagnosticListener 订阅者。连接及命令正常执行仍需实际验证。 |
| IL2026 | `Microsoft.Data.SqlClient.Diagnostics.SqlDiagnosticListener.WriteConnectionOpenError(Guid,SqlConnection,Exception,String)` | 诊断事件载荷属性保留；运行器没有注册 DiagnosticListener 订阅者。连接及命令正常执行仍需实际验证。 |
| IL2026 | `Microsoft.Data.SqlClient.Diagnostics.SqlDiagnosticListener.WriteConnectionOpenAfter(Guid,SqlConnection,String)` | 诊断事件载荷属性保留；运行器没有注册 DiagnosticListener 订阅者。连接及命令正常执行仍需实际验证。 |
| IL2026 | `Microsoft.Data.SqlClient.Diagnostics.SqlDiagnosticListener.WriteConnectionOpenBefore(SqlConnection,String)` | 诊断事件载荷属性保留；运行器没有注册 DiagnosticListener 订阅者。连接及命令正常执行仍需实际验证。 |
| IL2026 | `Microsoft.Data.SqlClient.SqlConfigurableRetryLogicLoader.Default_Resolving(AssemblyLoadContext,AssemblyName)` | 按配置反射加载自定义重试提供者；运行器未配置此扩展，但驱动初始化可能经过配置读取。 |
| IL2093 | `DuckDB.NET.Data.DuckDBDataReader.GetFieldType(Int32)` | DuckDB reader 字段类型的裁剪注解与基类不一致；基础 SQL 通过不代表所有字段类型保留。 |
| IL2093 | `DuckDB.NET.Data.DuckDBDataReader.GetProviderSpecificFieldType(Int32)` | DuckDB reader 字段类型的裁剪注解与基类不一致；基础 SQL 通过不代表所有字段类型保留。 |
| IL2072 | `Microsoft.Data.SqlClient.SqlConfigurableRetryLogicLoader.ResolveRetryLogicProvider(String,String,SqlRetryLogicOption)` | 按配置反射加载自定义重试提供者；运行器未配置此扩展，但驱动初始化可能经过配置读取。 |
| IL2057 | `Microsoft.Data.SqlClient.SqlConfigurableRetryLogicLoader.LoadType(String)` | 按配置反射加载自定义重试提供者；运行器未配置此扩展，但驱动初始化可能经过配置读取。 |
| IL2026 | `Microsoft.Data.SqlClient.Diagnostics.SqlDiagnosticListener.WriteCommandError(Guid,SqlCommand,SqlTransaction,Exception,String)` | 诊断事件载荷属性保留；运行器没有注册 DiagnosticListener 订阅者。连接及命令正常执行仍需实际验证。 |
| IL2026 | `Microsoft.Data.SqlClient.Diagnostics.SqlDiagnosticListener.WriteCommandAfter(Guid,SqlCommand,SqlTransaction,String)` | 诊断事件载荷属性保留；运行器没有注册 DiagnosticListener 订阅者。连接及命令正常执行仍需实际验证。 |
| IL2026 | `Microsoft.Data.SqlClient.Diagnostics.SqlDiagnosticListener.WriteCommandBefore(SqlCommand,SqlTransaction,String)` | 诊断事件载荷属性保留；运行器没有注册 DiagnosticListener 订阅者。连接及命令正常执行仍需实际验证。 |
| IL2026 | `Microsoft.Data.SqlClient.SqlConfigurableRetryLogicLoader.AssemblyResolver(AssemblyName)` | 按配置反射加载自定义重试提供者；运行器未配置此扩展，但驱动初始化可能经过配置读取。 |
| IL2026 | `Microsoft.Data.SqlClient.SqlConfigurableRetryLogicLoader.TypeResolver(Assembly,String,Boolean)` | 按配置反射加载自定义重试提供者；运行器未配置此扩展，但驱动初始化可能经过配置读取。 |
| IL2057 | `Microsoft.Data.SqlClient.SqlConnection.CheckGetExtendedUDTInfo(SqlMetaDataPriv,Boolean)` | SQL CLR UDT 的类型发现、构造和序列化；本轮基础 DDL/DML 验证不覆盖 CLR UDT 结果或值转换。 |
| IL3050 | `DuckDB.NET.Data.DataChunk.Reader.ListVectorDataReader.GetColumnType()` | DuckDB 集合、结构、nullable 类型的动态泛型、反射构造或表达式编译；基础升迁验证不覆盖这些 CLR 值转换，复杂查询结果需单独验证。 |
| IL3050 | `DuckDB.NET.Data.DataChunk.Reader.MapVectorDataReader.GetColumnType()` | DuckDB 集合、结构、nullable 类型的动态泛型、反射构造或表达式编译；基础升迁验证不覆盖这些 CLR 值转换，复杂查询结果需单独验证。 |
| IL2067 | `DuckDB.NET.Data.DataChunk.Reader.MapVectorDataReader.GetValue(UInt64,Type)` | DuckDB 集合、结构、nullable 类型的动态泛型、反射构造或表达式编译；基础升迁验证不覆盖这些 CLR 值转换，复杂查询结果需单独验证。 |
| IL2067 | `System.Configuration.TypeUtil.CreateInstance(Type)` | 配置类型的反射加载/构造，由 SqlClient 配置路径可达；需要实际验证默认驱动初始化，不能只检查本项目编译。 |
| IL2067 | `DuckDB.NET.Data.DataChunk.Reader.StructVectorDataReader.GetStruct(UInt64,Type)` | DuckDB 集合、结构、nullable 类型的动态泛型、反射构造或表达式编译；基础升迁验证不覆盖这些 CLR 值转换，复杂查询结果需单独验证。 |
| IL2070 | `DuckDB.NET.Data.DataChunk.Reader.StructVectorDataReader.<>c__DisplayClass5_0.<GetStruct>b__0(Type)` | DuckDB 集合、结构、nullable 类型的动态泛型、反射构造或表达式编译；基础升迁验证不覆盖这些 CLR 值转换，复杂查询结果需单独验证。 |
| IL2067 | `DuckDB.NET.Data.DataChunk.Reader.ListVectorDataReader.GetList(Type,UInt64,UInt64)` | DuckDB 集合、结构、nullable 类型的动态泛型、反射构造或表达式编译；基础升迁验证不覆盖这些 CLR 值转换，复杂查询结果需单独验证。 |
| IL2075 | `DuckDB.NET.Data.PreparedStatement.ClrToDuckDBConverter.CreateListFromClrType(ICollection,DbType)` | DuckDB 集合、结构、nullable 类型的动态泛型、反射构造或表达式编译；基础升迁验证不覆盖这些 CLR 值转换，复杂查询结果需单独验证。 |
| IL3050 | `DuckDB.NET.Data.DataChunk.Reader.VectorDataReaderBase.NullableHandler`1.Compile()` | DuckDB 集合、结构、nullable 类型的动态泛型、反射构造或表达式编译；基础升迁验证不覆盖这些 CLR 值转换，复杂查询结果需单独验证。 |
| IL2026 | `Microsoft.Data.SqlTypes.SqlVector`1.GetString()` | SQL vector/参数转换的反射 JSON；运行器提交脚本文本、不绑定 SqlParameter。复杂 SQL 返回值仍需按实际脚本验证。 |
| IL3050 | `Microsoft.Data.SqlTypes.SqlVector`1.GetString()` | SQL vector/参数转换的反射 JSON；运行器提交脚本文本、不绑定 SqlParameter。复杂 SQL 返回值仍需按实际脚本验证。 |
| IL2026 | `Microsoft.Data.SqlClient.SqlParameter.CoerceValue(Object,MetaType,Boolean&,Boolean&,Boolean)` | SQL vector/参数转换的反射 JSON；运行器提交脚本文本、不绑定 SqlParameter。复杂 SQL 返回值仍需按实际脚本验证。 |
| IL3050 | `Microsoft.Data.SqlClient.SqlParameter.CoerceValue(Object,MetaType,Boolean&,Boolean&,Boolean)` | SQL vector/参数转换的反射 JSON；运行器提交脚本文本、不绑定 SqlParameter。复杂 SQL 返回值仍需按实际脚本验证。 |
| IL2072 | `Microsoft.Data.SqlClient.Server.SerializationHelperSql9.Serialize(Stream,Object)` | SQL CLR UDT 的类型发现、构造和序列化；本轮基础 DDL/DML 验证不覆盖 CLR UDT 结果或值转换。 |
| IL2072 | `Microsoft.Data.SqlClient.Server.SerializationHelperSql9.SizeInBytes(Object)` | SQL CLR UDT 的类型发现、构造和序列化；本轮基础 DDL/DML 验证不覆盖 CLR UDT 结果或值转换。 |
| IL2057 | `Microsoft.Data.SqlClient.Server.SmiMetaData.Type.get` | SQL CLR UDT 的类型发现、构造和序列化；本轮基础 DDL/DML 验证不覆盖 CLR UDT 结果或值转换。 |
| IL2072 | `Microsoft.Data.SqlClient.Server.MetaDataUtilsSmi.SqlMetaDataToSmiExtendedMetaData(SqlMetaData)` | SQL CLR UDT 的类型发现、构造和序列化；本轮基础 DDL/DML 验证不覆盖 CLR UDT 结果或值转换。 |
| IL2111 | `DuckDB.NET.Data.DuckDBDataReader.GetSchemaTable()` | reader schema 表的 Type 属性反射；运行器没有调用 GetSchemaTable，不声明其他调用方支持 AOT。 |
| IL2111 | `MySqlConnector.MySqlDataReader.BuildSchemaTable()` | reader schema 表的 Type 属性反射；运行器没有调用 GetSchemaTable，不声明其他调用方支持 AOT。 |
| IL2111 | `Microsoft.Data.SqlClient.SqlDataReader.BuildSchemaTable()` | reader schema 表的 Type 属性反射；运行器没有调用 GetSchemaTable，不声明其他调用方支持 AOT。 |
| IL2111 | `Microsoft.Data.SqlClient.SqlDataReader.BuildSchemaTable()` | reader schema 表的 Type 属性反射；运行器没有调用 GetSchemaTable，不声明其他调用方支持 AOT。 |
| IL2057 | `Microsoft.Data.SqlClient.SqlAuthenticationProviderManager.SqlAuthenticationProviderManager(SqlAuthenticationProviderConfigurationSection)` | 按名称反射构造身份验证提供者；运行器使用 SQL 用户名/密码，仍需验证默认初始化没有失败。 |
| IL2057 | `Microsoft.Data.SqlClient.SqlAuthenticationProviderManager.SqlAuthenticationProviderManager(SqlAuthenticationProviderConfigurationSection)` | 按名称反射构造身份验证提供者；运行器使用 SQL 用户名/密码，仍需验证默认初始化没有失败。 |
| IL2072 | `Microsoft.Data.SqlClient.Server.BinaryOrderedUdtNormalizer.BinaryOrderedUdtNormalizer(Type,Boolean)` | SQL CLR UDT 的类型发现、构造和序列化；本轮基础 DDL/DML 验证不覆盖 CLR UDT 结果或值转换。 |
| IL2075 | `Microsoft.Data.SqlClient.Server.ValueUtilsSmi.GetUdt_LengthChecked(ITypedGettersV3,Int32,SmiMetaData)` | SQL CLR UDT 的类型发现、构造和序列化；本轮基础 DDL/DML 验证不覆盖 CLR UDT 结果或值转换。 |
| IL2072 | `Microsoft.Data.SqlClient.Server.ValueUtilsSmi.GetUdt_LengthChecked(ITypedGettersV3,Int32,SmiMetaData)` | SQL CLR UDT 的类型发现、构造和序列化；本轮基础 DDL/DML 验证不覆盖 CLR UDT 结果或值转换。 |
| IL2026 | `Microsoft.Data.SqlClient.HostGuardianServiceEnclaveProvider.MakeRequest(String)` | Always Encrypted 安全区域证明的反射 JSON；运行器未配置此功能，不据此声明该功能可用。 |
| IL3050 | `Microsoft.Data.SqlClient.HostGuardianServiceEnclaveProvider.MakeRequest(String)` | Always Encrypted 安全区域证明的反射 JSON；运行器未配置此功能，不据此声明该功能可用。 |
| IL2070 | `System.Configuration.TypeUtil.GetConstructor(Type,Type,Boolean)` | 配置类型的反射加载/构造，由 SqlClient 配置路径可达；需要实际验证默认驱动初始化，不能只检查本项目编译。 |
| IL2075 | `Microsoft.Data.SqlClient.Server.ValueUtilsSmi.NullUdtInstance(SmiMetaData)` | SQL CLR UDT 的类型发现、构造和序列化；本轮基础 DDL/DML 验证不覆盖 CLR UDT 结果或值转换。 |
| IL2072 | `Microsoft.Data.SqlClient.Server.BinaryOrderedUdtNormalizer.DeNormalize(FieldInfo,Object,Stream)` | SQL CLR UDT 的类型发现、构造和序列化；本轮基础 DDL/DML 验证不覆盖 CLR UDT 结果或值转换。 |
| IL2057 | `System.Configuration.TypeUtil.GetType(String,Boolean)` | 配置类型的反射加载/构造，由 SqlClient 配置路径可达；需要实际验证默认驱动初始化，不能只检查本项目编译。 |
| IL2026 | `System.Configuration.TypeUtil.GetImplicitType(String)` | 配置类型的反射加载/构造，由 SqlClient 配置路径可达；需要实际验证默认驱动初始化，不能只检查本项目编译。 |
| IL2057 | `System.Configuration.TypeUtil.GetImplicitType(String)` | 配置类型的反射加载/构造，由 SqlClient 配置路径可达；需要实际验证默认驱动初始化，不能只检查本项目编译。 |
