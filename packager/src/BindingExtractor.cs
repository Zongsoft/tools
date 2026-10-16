/*
 *   _____                                ______
 *  /_   /  ____  ____  ____  _________  / __/ /_
 *    / /  / __ \/ __ \/ __ \/ ___/ __ \/ /_/ __/
 *   / /__/ /_/ / / / /_/ /\_ \/ /_/ / __/ /_
 *  /____/\____/_/ /_/\__  /____/\____/_/  \__/
 *                   /____/
 *
 * Authors:
 *   钟峰(Popeye Zhong) <zongsoft@gmail.com>
 *
 * The MIT License (MIT)
 *
 * Copyright (C) 2026 Zongsoft Corporation <http://www.zongsoft.com>
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 */

using System;
using System.IO;
using System.Xml;
using System.Linq;
using System.Collections.Generic;

using Microsoft.Extensions.Configuration;

using Zongsoft.Communication;
using Zongsoft.Configuration;
using Zongsoft.Configuration.Xml;
using Zongsoft.Text.Templating;

namespace Zongsoft.Tools.Packager;

/// <summary>从最终安装载荷逐文件提取 zongsoft.tools.binding 声明并归并同一服务的替代端点。</summary>
internal static partial class BindingExtractor
{
	#region 常量定义
	private const string INSTRUCTION_NAME = "zongsoft.tools.binding";
	#endregion

	#region 公共方法
	public static BindingDocument Extract(Package package)
	{
		ArgumentNullException.ThrowIfNull(package);
		var files = new PackageFiles(package);
		var groups = new HashSet<BindingDocument.Binding>();
		var endpoints = new Dictionary<string, BindingDocument.Binding>(StringComparer.Ordinal);

		foreach(var path in files.Options)
		{
			var source = new XmlConfigurationSource { Path = path, FileProvider = files, ReloadOnChange = false };
			source.Options.Instructions.Processing = ProcessInstruction;
			var provider = new XmlConfigurationProvider(source);

			try
			{
				using var configuration = new ConfigurationRoot([provider]);
				var view = new ConfigurationView(provider);

				foreach(var instruction in provider.Instructions.Where(item => item.Name.Equals(INSTRUCTION_NAME, StringComparison.OrdinalIgnoreCase)))
				{
					foreach(var binding in Extract(configuration, provider, view, instruction))
						Merge(binding, groups, endpoints);
				}
			}
			catch(ExtractionException)
			{
				throw;
			}
			catch(Exception exception) when(exception is IOException or FormatException or ArgumentException or InvalidOperationException or XmlException or ConfigurationException)
			{
				// 文件配置基类可能包装加载异常，保留提取器已记录的精确 PI 来源
				for(var inner = exception.InnerException; inner != null; inner = inner.InnerException)
				{
					if(inner is ExtractionException extraction)
						throw extraction;
				}

				throw Error(path, "configuration", "read");
			}
			finally
			{
				// ConfigurationRoot 构造失败时仍释放文件提供程序；Dispose 支持重复调用。
				provider.Dispose();
			}
		}

		var document = new BindingDocument { Bindings = groups.OrderBy(binding => binding.Id, StringComparer.Ordinal).ToArray() };
		_ = document.Serialize();

		return document;
	}
	#endregion

	#region 提取方法
	private static IEnumerable<BindingDocument.Binding> Extract(IConfigurationRoot configuration, IConfigurationProvider provider, ConfigurationView view, XmlConfigurationInstruction instruction)
	{
		var attributes = ReadAttributes();

		var path = GetRequired(attributes, "path", instruction);
		var template = GetRequired(attributes, "template", instruction);
		var collection = path.EndsWith("/*", StringComparison.Ordinal);
		var selected = collection ? path[..^2] : path;

		if(selected != "" && selected != "/" && (selected.Trim('/').Split('/').Any(part => part is "" or "." or "..") || selected.Contains('*') || selected.Contains('\\') || selected.Contains(':') || selected.Any(char.IsControl)))
			throw Error(instruction, path, "path", "syntax");

		attributes.TryGetValue("name", out var name);
		attributes.TryGetValue("protocols", out var protocols);
		attributes.TryGetValue("filter", out var filterText);
		var required = false;

		if(attributes.TryGetValue("required", out var requiredText) && !bool.TryParse(requiredText, out required))
			throw Error(instruction, path, "required", "boolean");
		if(name != null && string.IsNullOrWhiteSpace(name))
			throw Error(instruction, path, "name", "empty");
		if(protocols != null && string.IsNullOrWhiteSpace(protocols))
			throw Error(instruction, path, "protocols", "empty");

		var separators = attributes.TryGetValue("separators", out var separatorText) ? separatorText : ",;|";

		if(separators.Length == 0)
			throw Error(instruction, path, "separators", "empty");

		var filter = ReadFilter();

		var candidates = GetCandidates();
		var result = new List<BindingDocument.Binding>();

		foreach(var candidate in candidates)
		{
			ReadCandidate(candidate);
		}

		if(required && result.Count == 0)
			throw Error(instruction, path, "required", "empty-result");

		return result;

		void ReadCandidate(IConfiguration candidate)
		{
			view.Path = candidate is IConfigurationSection section ? section.Path : "";
			var alias = view.Path.Length == 0 ? "/" : view.Path.Replace(':', '/');

			try
			{
				if(!filter.Evaluate(view.Match))
					return;
			}
			catch(Exception exception) when(exception is FormatException or ArgumentException)
			{
				throw Error(instruction, alias, "filter/" + view.Reference, "field");
			}

			var addressText = Evaluate(view, template, instruction, alias, "template");
			var protocolText = protocols == null ? null : Evaluate(view, protocols, instruction, alias, "protocols");
			if(protocolText != null && string.IsNullOrWhiteSpace(protocolText))
				throw Error(instruction, alias, "protocols", "empty");

			var capabilities = protocolText == null ? [] : protocolText.Split(',', StringSplitOptions.TrimEntries);
			if(capabilities.Any(capability => !NetworkProtocol.TryParse(capability, out _)))
				throw Error(instruction, alias, "protocols", "protocol");
			if(string.IsNullOrWhiteSpace(addressText))
				return;

			var aliases = new List<string> { alias };

			if(name != null)
			{
				aliases.Add(name.Trim());
				if(collection && candidate is IConfigurationSection item)
					aliases.Add(name.Trim() + "/" + item.Key);
			}

			var values = ReadEndpoints(addressText, capabilities, alias);

			foreach(var group in values.GroupBy(endpoint => (endpoint.Path, Protocols: string.Join(',', endpoint.Protocols))))
				result.Add(BindingDocument.CreateBinding("application", aliases.ToArray(), group.ToArray()));
		}

		IReadOnlyDictionary<string, string> ReadAttributes()
		{
			IReadOnlyDictionary<string, string> attributes;

			try
			{
				attributes = instruction.GetAttributes();
			}
			catch(FormatException)
			{
				throw Error(instruction, "/", "attributes", "syntax");
			}

			foreach(var attribute in attributes)
			{
				if(!new[] { "name", "path", "template", "protocols", "filter", "separators", "required" }.Contains(attribute.Key, StringComparer.OrdinalIgnoreCase))
					throw Error(instruction, "/", attribute.Key, "unknown");
			}

			return attributes;
		}

		FilterExpression ReadFilter()
		{
			FilterExpression filter;

			try
			{
				filter = FilterExpression.Parse(filterText, ConfigurationView.ValidateField);
			}
			catch(FormatException)
			{
				throw Error(instruction, path, "filter", "syntax");
			}

			return filter;
		}

		IEnumerable<IConfiguration> GetCandidates()
		{
			var key = ConfigurationUtility.GetConfigurationPath(selected);

			if(collection)
				return key.Length == 0 ? configuration.GetChildren() : configuration.GetSection(key).GetChildren();
			if(key.Length == 0)
				return [configuration];

			return provider.TryGet(key, out _) || configuration.GetSection(key).GetChildren().Any() ? [configuration.GetSection(key)] : [];
		}

		List<BindingDocument.Endpoint> ReadEndpoints(string addressText, string[] capabilities, string alias)
		{
			var values = new List<BindingDocument.Endpoint>();
			foreach(var value in addressText.Split(separators.ToCharArray(), StringSplitOptions.TrimEntries))
			{
				if(!NetworkAddress.TryParse(value, out var address))
					throw Error(instruction, alias, "template", "address");

				try
				{
					var binding = BindingDocument.CreateBinding("application", [],
					[
						new(address.Scheme,
							address.Host + (address.Zone == null ? "" : "%" + address.Zone),
							address.Port,
							address.Path,
							capabilities)
					]);

					values.Add(binding.Endpoints[0]);
				}
				catch(Exception exception) when(exception is InvalidDataException or ArgumentException)
				{
					throw Error(instruction, alias, "template/protocols", "endpoint");
				}
			}

			return values;
		}
	}
	#endregion

	#region 辅助方法
	private static string GetRequired(IReadOnlyDictionary<string, string> attributes, string name, XmlConfigurationInstruction instruction) =>
		attributes.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : throw Error(instruction, "/", name, "required");

	private static string Evaluate(ConfigurationView view, string template, XmlConfigurationInstruction instruction, string path, string field)
	{
		try
		{
			return view.Evaluate(template);
		}
		catch(TemplateEvaluationException)
		{
			throw Error(instruction, path, field + "/" + view.Reference, "template");
		}
	}

	private static void Merge(BindingDocument.Binding binding, HashSet<BindingDocument.Binding> groups, Dictionary<string, BindingDocument.Binding> endpoints)
	{
		var matches = binding.Endpoints.Where(endpoint => endpoints.ContainsKey(endpoint.Id)).Select(endpoint => endpoints[endpoint.Id]).Distinct().ToArray();

		if(matches.Length > 0)
		{
			binding = BindingDocument.CreateBinding(binding.Owner,
				matches.SelectMany(item => item.Aliases).Concat(binding.Aliases).ToArray(),
				matches.SelectMany(item => item.Endpoints).Concat(binding.Endpoints).ToArray());

			foreach(var match in matches)
				groups.Remove(match);
		}

		groups.Add(binding);
		foreach(var endpoint in binding.Endpoints)
			endpoints[endpoint.Id] = binding;
	}

	private static ExtractionException Error(string file, string field, string rule) => new(file, 0, 0, "/", field, rule);
	private static ExtractionException Error(XmlConfigurationInstruction instruction, string path, string field, string rule) =>
		new(instruction.FilePath, instruction.LineNumber, instruction.LinePosition, path, field, rule);
	#endregion

	#region 嵌套类型
	private sealed class ExtractionException(string file, int line, int column, string path, string field, string rule) :
		Exception(string.Format(Properties.Resources.BindingExtraction_Message, file, line, column, path, field, rule));
	#endregion
}
