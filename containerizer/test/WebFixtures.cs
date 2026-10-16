using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;

namespace Zongsoft.Tools.Containerizer.Tests;

internal static class WebFixtures
{
	internal static BindingDocument CreateDocument(params Site[] definitions)
	{
		var application = BindingDocument.CreateBinding("application", ["web"], [new("http", "0.0.0.0", 8069, "/", ["http1"])]);
		var bindings = new List<BindingDocument.Binding> { application };
		var sites = new List<BindingDocument.Site>();

		foreach(var definition in definitions)
		{
			var listeners = new List<BindingDocument.SiteListener>();
			foreach(var address in definition.Addresses)
			{
				var uri = new Uri(address);
				var binding = BindingDocument.CreateBinding("nginx", [], [new(uri.Scheme, uri.Host.Trim('[', ']'), uri.Port, "/", ["http1"])]);
				if(!bindings.Any(candidate => candidate.Id == binding.Id))
					bindings.Add(binding);
				listeners.Add(new(binding.Id, binding.Endpoints[0].Id, definition.Default));
			}

			sites.Add(new("site:" + definition.Name, definition.Name, definition.Hosts ?? [], [.. listeners],
				[new("http", "/", new() { Kind = "binding", Binding = application.Id, Endpoint = application.Endpoints[0].Id }, "http1", new("http", "127.0.0.1", 8069, "/"))]));
		}

		return new() { Bindings = [.. bindings], Sites = [.. sites] };
	}

	internal static string Template(BindingDocument document)
	{
		var application = document.Bindings.Single(binding => binding.Owner == "application");
		return "server { listen 8080; location / { proxy_pass http://{{zongsoft:binding:" + application.Id + ":" + application.Endpoints[0].Id + "}}; } }";
	}

	internal sealed record Site(string Name, string[] Addresses, string[] Hosts = null, bool Default = false);

	internal static string WritePackage(string path, string name, BindingDocument bindings, string template, Dictionary<string, string> files = null)
	{
		var entries = new Dictionary<string, string>
		{
			[".bindings"] = Encoding.UTF8.GetString((bindings ?? new()).Serialize()),
			[$".root/etc/systemd/system/{name}.service"] = $"[Service]\nWorkingDirectory=/opt/{name}\nExecStart=/opt/{name}/{name} --urls http://127.0.0.1:8069\n",
			[$".web/nginx/{name}.conf"] = "server { listen 8080; }\n",
		};

		if(template != null)
			entries[$".web/nginx/{name}.conf.template"] = template;

		if(files != null)
		{
			foreach(var pair in files)
				entries[pair.Key] = pair.Value;
		}

		return PackageFixtures.Write(path, name, entries);
	}
}
