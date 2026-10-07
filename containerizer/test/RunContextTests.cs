using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Xunit;
using Zongsoft.Components;
using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer.Tests;

[Collection("Run sessions")]
public sealed class RunContextTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), $"containerizer-run-test-{Guid.NewGuid():N}");

	public RunContextTests() => Directory.CreateDirectory(_root);
	public void Dispose() => Directory.Delete(_root, true);

	[Theory]
	[InlineData("docker", 0, false, 0)]
	[InlineData("podman", 17, false, 4)]
	[InlineData("podman", 0, true, 4)]
	public async Task SessionWaitsForCancellationAndOnlyRemovesOwnedResourcesAsync(string executable, int installExit, bool cleanupFailure, int expected)
	{
		using var bundle = this.CreateBundle();
		using var stopping = new CancellationTokenSource(TimeSpan.FromSeconds(15));

		var runner = new Runner { CleanupFailure = cleanupFailure, InstallExit = installExit };
		var messages = new List<string>();
		var retained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var run = new RunContext(new(executable, runner), bundle, content =>
		{
			var message = new StringBuilder();
			for(var item = content.First; item != null; item = item.Next)
				message.Append(item.Text);

			messages.Add(message.ToString());
			if(HasColor(content, CommandOutletColor.Green) || HasColor(content, CommandOutletColor.Magenta))
				retained.TrySetResult();
		});

		var original = Files.Hash(Path.Combine(bundle.Directory, DeliveryPlan.FileName));
		var task = run.ExecuteAsync(stopping.Token);
		await retained.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

		Assert.False(task.IsCompleted);
		Assert.True(runner.Owned);
		var instructions = Assert.Single(messages, message => message.Contains("docker ps -a", StringComparison.Ordinal));
		Assert.Contains($"\n  {executable} exec {runner.SessionName} docker ps -a", instructions);
		Assert.Contains($"\n  {executable} exec {runner.SessionName} docker logs <", instructions);

		stopping.Cancel();
		Assert.Equal(expected, await task);
		Assert.Equal(executable, Assert.Single(runner.Streams).Executable);
		Assert.Equal(cleanupFailure, runner.Owned);
		Assert.Equal(original, Files.Hash(Path.Combine(bundle.Directory, DeliveryPlan.FileName)));
		Assert.False(Directory.Exists(runner.Workspace));
		Assert.All(runner.Calls.Where(call => call[0] == "rm"), call => Assert.Equal(["rm", "--force", "--volumes", "owned-id"], call));
		Assert.DoesNotContain(runner.Calls, call => call.Contains("prune") || call.Contains("unrelated-id") || call.Contains("/var/run/docker.sock"));
	}

	[Fact]
	public async Task ImageCleanupTimeoutDoesNotCancelOuterContainerCleanupAsync()
	{
		using var bundle = this.CreateBundle();
		using var stopping = new CancellationTokenSource(TimeSpan.FromSeconds(15));

		var runner = new Runner { ImageCleanupTimeout = true };
		var run = new RunContext(new("podman", runner), bundle, content =>
		{
			if(HasColor(content, CommandOutletColor.Green))
				stopping.Cancel();
		});

		Assert.Equal(0, await run.ExecuteAsync(stopping.Token));
		Assert.False(runner.Owned);
		Assert.Single(runner.Calls, call => call[0] == "rm");
	}

	[Theory]
	[InlineData("zh-CN", "zh_CN.UTF-8")]
	[InlineData("en-US", "en_US.UTF-8")]
	[InlineData("", "C.UTF-8")]
	public async Task InstallerUsesTheLocalInterfaceLanguageAsync(string culture, string locale)
	{
		var original = CultureInfo.CurrentUICulture;

		try
		{
			CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
			using var bundle = this.CreateBundle();
			using var stopping = new CancellationTokenSource(TimeSpan.FromSeconds(15));
			var runner = new Runner();

			var run = new RunContext(new("podman", runner), bundle, content =>
			{
				if(HasColor(content, CommandOutletColor.Green))
					stopping.Cancel();
			});

			Assert.Equal(0, await run.ExecuteAsync(stopping.Token));
			Assert.Equal(["exec", "--env", $"LANG={locale}", "--workdir", "/delivery", runner.SessionName, "sh", "/delivery/install.sh"], Assert.Single(runner.Streams).Arguments);
			Assert.DoesNotContain(runner.Calls, call => call[0] == "create" && call.Contains("--env"));
		}
		finally
		{
			CultureInfo.CurrentUICulture = original;
		}
	}

	[Fact]
	public async Task RunPassesTemporaryMirrorRulesWithoutChangingDeliveryAssetsAsync()
	{
		using var bundle = this.CreateBundle();
		using var stopping = new CancellationTokenSource(TimeSpan.FromSeconds(15));

		var runner = new Runner();
		using var preparing = new CancellationTokenSource(TimeSpan.FromSeconds(15));
		var preparation = new RunContext(new("podman", runner), bundle, content =>
		{
			if(HasColor(content, CommandOutletColor.Green))
				preparing.Cancel();
		});
		Assert.Equal(0, await preparation.ExecuteAsync(preparing.Token));
		runner.Calls.Clear();
		runner.Streams.Clear();
		var mirrors = new RegistryMirrors { Registries = new() { ["docker.io"] = ["mirror.example.com/docker.io"] } };
		var engine = new ContainerEngine("podman", runner) { Mirrors = mirrors };
		var hash = Files.Hash(Path.Combine(bundle.Directory, DeliveryPlan.FileName));

		var run = new RunContext(engine, bundle, content =>
		{
			if(HasColor(content, CommandOutletColor.Green))
				stopping.Cancel();
		});

		Assert.Equal(0, await run.ExecuteAsync(stopping.Token));
		Assert.Contains($"{RegistryMirrors.ENVIRONMENT_VARIABLE}=/run/containerizer-mirrors.json", Assert.Single(runner.Streams).Arguments);

		var copy = Assert.Single(runner.Calls, call => call[0] == "cp" && call[^1].EndsWith("/run/containerizer-mirrors.json", StringComparison.Ordinal));
		Assert.False(File.Exists(copy[1]));
		Assert.Equal(hash, Files.Hash(Path.Combine(bundle.Directory, DeliveryPlan.FileName)));
		Assert.DoesNotContain(bundle.Plan.Files, file => file.Path.Contains("mirrors", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task CancellationDuringCreateOrInstallUsesAnIndependentCleanupTokenAsync(bool duringCreate)
	{
		using var bundle = this.CreateBundle();
		using var stopping = new CancellationTokenSource(TimeSpan.FromSeconds(15));

		var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var runner = new Runner { CancelCreate = duringCreate ? stopping : null, InstallStarted = started, WaitForInstallCancellation = true };
		var run = new RunContext(new("podman", runner), bundle, _ => { });
		var task = run.ExecuteAsync(stopping.Token);

		if(!duringCreate)
		{
			await started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
			stopping.Cancel();
		}

		Assert.Equal(130, await task);
		Assert.False(runner.Owned);
		Assert.Single(runner.Calls, call => call[0] == "rm");
	}

	[Theory]
	[InlineData("existing")]
	[InlineData("architecture")]
	public async Task PreflightFailuresCannotDeleteAnotherSessionAsync(string failure)
	{
		using var bundle = this.CreateBundle();
		var runner = new Runner { Existing = failure == "existing", Architecture = failure == "architecture" ? "linux/arm64" : "linux/amd64" };
		var run = new RunContext(new("podman", runner), bundle, _ => { });

		await Assert.ThrowsAsync<ContainerizationException>(() => run.ExecuteAsync(CancellationToken.None));
		Assert.DoesNotContain(runner.Calls, call => call[0] is "create" or "rm");
	}

	[Fact]
	public async Task EnginePortRaceRetriesOnceWithoutReinstallingAsync()
	{
		using var bundle = this.CreateBundle();
		using var stopping = new CancellationTokenSource(TimeSpan.FromSeconds(15));

		var runner = new Runner { PortConflict = true };
		var run = new RunContext(new("docker", runner), bundle, content =>
		{
			if(HasColor(content, CommandOutletColor.Green))
				stopping.Cancel();
		});

		Assert.Equal(0, await run.ExecuteAsync(stopping.Token));
		Assert.Equal(2, runner.Calls.Count(call => call[0] == "create"));
		Assert.Single(runner.Streams);
		Assert.False(runner.Owned);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task LocalHttpProbePinsSocketAndAccepts404AndExternalRedirectsAsync(bool redirect)
	{
		using var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();

		var port = ((IPEndPoint)listener.LocalEndpoint).Port;
		using var bundle = this.CreateBundle();
		bundle.Plan.Services[0].Id = "web";
		bundle.Plan.Services[0].Web.Add(CreateWebSite());
		bundle.Plan.Services[0].Ports.Add(new() { Name = "web", Host = port, Container = 80 });
		var runner = new Runner { PublishedHostPort = port };
		var messages = new List<string>();

		var serving = Task.Run(async () =>
		{
			using var client = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
			using var stream = client.GetStream();

			var buffer = new byte[2048];
			var count = await stream.ReadAsync(buffer, TestContext.Current.CancellationToken);

			Assert.True(count > 0);
			Assert.Contains("Host: example.invalid:" + port, Encoding.ASCII.GetString(buffer, 0, count));
			var response = redirect ? "HTTP/1.1 302 Found\r\nLocation: https://example.invalid/\r\n" : "HTTP/1.1 404 Not Found\r\n";
			await stream.WriteAsync(Encoding.ASCII.GetBytes($"{response}Content-Length: 0\r\nConnection: close\r\n\r\n"), TestContext.Current.CancellationToken);
		}, TestContext.Current.CancellationToken);

		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		var run = new RunContext(new("podman", runner), bundle, content =>
		{
			messages.Add(GetText(content));
			if(HasColor(content, CommandOutletColor.Green))
				timeout.Cancel();
		});
		Assert.Equal(0, await run.ExecuteAsync(timeout.Token));
		Assert.Contains(messages, message => message.Contains($"http://example.invalid:{port}/", StringComparison.Ordinal));
		Assert.Equal(redirect, messages.Any(message => message.Contains("https://example.invalid/", StringComparison.Ordinal)));
		Assert.Contains("0.0.0.0::45000/tcp", Assert.Single(runner.Calls, call => call[0] == "create"));

		await serving;
	}

	[Theory]
	[InlineData(false, 0)]
	[InlineData(true, 4)]
	public async Task MissingLocalMappingIsOptionalForInfrastructureButRequiredForWebAsync(bool web, int expected)
	{
		using var bundle = this.CreateBundle();
		bundle.Plan.Services[0].Ports.Add(new() { Name = "web", Host = 8080, Container = 80 });
		if(web)
			bundle.Plan.Services[0].Web.Add(CreateWebSite());
		using var stopping = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		var warnings = 0;

		var run = new RunContext(new("podman", new Runner()), bundle, content =>
		{
			if(HasColor(content, CommandOutletColor.Magenta))
				warnings++;
			if(HasColor(content, CommandOutletColor.Green) || web && warnings != 0)
				stopping.Cancel();
		});

		Assert.Equal(expected, await run.ExecuteAsync(stopping.Token));
		Assert.Equal(1, warnings);
	}

	[Fact]
	public async Task ForwardingRelaysAvoidDeclaredPortsAndNeverAddUnpublishedServicesAsync()
	{
		using var bundle = this.CreateBundle();
		bundle.Plan.Services.Add(new() { Id = "web", Image = bundle.Plan.Services[0].Image, Ports = [new() { Host = 45000, Container = 80 }, new() { Host = 45001, Container = 53, Protocol = "udp" }] });
		using var stopping = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		var runner = new Runner();
		var run = new RunContext(new("podman", runner), bundle, content =>
		{
			if(HasColor(content, CommandOutletColor.Green))
				stopping.Cancel();
		});

		Assert.Equal(0, await run.ExecuteAsync(stopping.Token));
		var creation = Assert.Single(runner.Calls, call => call[0] == "create");
		Assert.Single(creation, argument => argument == "--publish");
		Assert.EndsWith(":45002/tcp", creation[Array.IndexOf(creation, "--publish") + 1]);
	}

	[Fact]
	public async Task HttpsProbePinsTheSocketPreservesSniAndRejectsUntrustedCertificatesAsync()
	{
		using var key = System.Security.Cryptography.RSA.Create(2048);
		var request = new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=example.invalid", key, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
		using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
		using var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();

		var port = ((IPEndPoint)listener.LocalEndpoint).Port;
		var site = CreateWebSite();
		site.Bindings[0].Scheme = "https";
		using var bundle = this.CreateBundle();
		bundle.Plan.Services[0].Id = "web";
		bundle.Plan.Services[0].Web.Add(site);
		bundle.Plan.Services[0].Ports.Add(new() { Name = "web", Host = port, Container = 80 });
		var runner = new Runner { PublishedHostPort = port };
		using var stopping = new CancellationTokenSource(TimeSpan.FromSeconds(25));
		var ready = false;
		var run = new RunContext(new("podman", runner), bundle, content =>
		{
			ready |= HasColor(content, CommandOutletColor.Green);
			if(HasColor(content, CommandOutletColor.Magenta))
				stopping.Cancel();
		});
		var names = new System.Collections.Concurrent.ConcurrentBag<string>();
		var serving = Task.Run(async () =>
		{
			try
			{
				while(!stopping.IsCancellationRequested)
				{
					using var client = await listener.AcceptTcpClientAsync(stopping.Token);
					using var stream = new System.Net.Security.SslStream(client.GetStream());

					try
					{
						await stream.AuthenticateAsServerAsync(new System.Net.Security.SslServerAuthenticationOptions
						{
							ServerCertificateSelectionCallback = (_, name) => { names.Add(name); return certificate; },
						}, stopping.Token);
					}
					catch(Exception exception) when(exception is System.Security.Authentication.AuthenticationException or IOException) { }
				}
			}
			catch(OperationCanceledException) when(stopping.IsCancellationRequested) { }
		}, TestContext.Current.CancellationToken);

		try
		{
			Assert.Equal(4, await run.ExecuteAsync(stopping.Token));
			Assert.NotEmpty(names);
			Assert.All(names, name => Assert.Equal("example.invalid", name));
			Assert.False(ready);
		}
		finally
		{
			stopping.Cancel();
			await serving;
		}
	}

	private static WebSitePlan CreateWebSite() => new() { Application = "web", Name = "api", ProbeHosts = ["example.invalid"], Bindings = [new() { Scheme = "http", Address = "0.0.0.0", Port = 80, Publication = "web" }] };

	private static string GetText(CommandOutletContent content)
	{
		var text = new StringBuilder();
		for(var item = content.First; item != null; item = item.Next)
			text.Append(item.Text);
		return text.ToString();
	}

	private static bool HasColor(CommandOutletContent content, CommandOutletColor color)
	{
		for(var item = content.First; item != null; item = item.Next)
		{
			if(item.ForegroundColor == color)
				return true;
		}

		return false;
	}

	private DeliveryBundle CreateBundle()
	{
		var name = $"run-test-{Guid.NewGuid():N}";
		var plan = new DeliveryPlan
		{
			Name = name,
			Architecture = "x64",
			Distribution = "debian@13",
			Project = $"containerizer-{name}-{Files.HashText(name)[..8]}",
			DataRoot = Installation.Paths.GetDataPath(name),
		};

		plan.Services.Add(new() { Id = "redis", Image = new() { Id = $"sha256:{new string('a', 64)}", Platform = "linux/amd64", Mode = "online", Reference = $"containerizer/{plan.Project}/redis:fixture" } });
		string[] files = ["containerizer", "compose.yaml", "install.sh", "uninstall.sh"];

		foreach(var file in files)
		{
			var path = Path.Combine(_root, file);
			Files.Write(path, "fixture");
			plan.Files.Add(new() { Path = file, Length = new FileInfo(path).Length, Hash = Files.Hash(path) });
		}

		Files.Save(Path.Combine(_root, DeliveryPlan.FileName), plan, ProtocolJson.Default.DeliveryPlan);
		Files.Write(Path.Combine(_root, "checksums.sha256"), $"{Files.Hash(Path.Combine(_root, DeliveryPlan.FileName))}  {DeliveryPlan.FileName}\n{string.Join('\n', plan.Files.Select(file => $"{file.Hash}  {file.Path}"))}\n");
		return DeliveryBundle.Open(_root);
	}

	private sealed class Runner : IProcessRunner
	{
		public List<string[]> Calls { get; } = [];
		public List<(string Executable, string[] Arguments)> Streams { get; } = [];
		public int InstallExit { get; init; }
		public TaskCompletionSource InstallStarted { get; init; }
		public bool WaitForInstallCancellation { get; init; }
		public bool Owned { get; private set; }
		public bool CleanupFailure { get; init; }
		public bool ImageCleanupTimeout { get; init; }
		public bool Existing { get; init; }
		public bool PortConflict { get; set; }
		public Dictionary<string, string> BaseImageLabels { get; } = [];
		public int PublishedHostPort { get; init; }
		public string Architecture { get; init; } = "linux/amd64";
		public string Workspace { get; private set; }
		public string SessionName { get; private set; }
		public CancellationTokenSource CancelCreate { get; init; }

		public async Task<int> StreamAsync(string executable, IReadOnlyList<string> arguments, string directory, CancellationToken cancellation)
		{
			this.Streams.Add((executable, arguments.ToArray()));
			this.InstallStarted?.TrySetResult();
			if(this.WaitForInstallCancellation)
				await Task.Delay(Timeout.Infinite, cancellation);
			return this.InstallExit;
		}

		public Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, string directory, CancellationToken cancellation, int timeoutSeconds = 900)
		{
			this.Calls.Add(arguments.ToArray());
			var output = "";
			if(this.ImageCleanupTimeout && arguments.Count > 3 && arguments[0] == "exec" && arguments[2] == "docker" && arguments[3] == "ps")
				throw new OperationCanceledException(cancellation);

			if(arguments[0] == "info")
				output = this.Architecture;
			if(this.BaseImageLabels.Count > 0 && arguments.Take(2).SequenceEqual(["image", "ls"]))
				output = "base-image-id";
			if(this.BaseImageLabels.Count > 0 && arguments.Take(2).SequenceEqual(["image", "inspect"]))
				output = JsonSerializer.Serialize(new[] { new { Config = new { Labels = this.BaseImageLabels } } });
			if(arguments[0] == "ps")
				output = arguments[^1] == "{{.Names}}" ? this.Existing ? "other-session" : "" : this.Owned ? "owned-id" : "";

			if(arguments[0] == "build" || arguments.Take(2).SequenceEqual(["buildx", "build"]))
			{
				this.Workspace = directory;

				foreach(var label in File.ReadLines(Path.Combine(directory, "Dockerfile")).Where(line => line.StartsWith("LABEL ", StringComparison.Ordinal)))
				{
					var pair = label[6..].Split('=', 2);
					this.BaseImageLabels[pair[0]] = pair[1];
				}
			}

			if(arguments[0] == "create")
			{
				this.Owned = true;
				this.SessionName = arguments[2];

				if(this.CancelCreate != null)
				{
					this.CancelCreate.Cancel();
					throw new OperationCanceledException(cancellation);
				}
			}

			if(arguments[0] == "start" && this.PortConflict)
			{
				this.PortConflict = false;
				return Task.FromResult(new ProcessResult(1, "", "port is already allocated"));
			}

			if(arguments[0] == "rm")
			{
				Assert.False(cancellation.IsCancellationRequested);

				if(this.CleanupFailure)
					return Task.FromResult(new ProcessResult(1, "", "fixture cleanup failure"));

				this.Owned = false;
			}

			if(arguments[0] == "inspect")
				output = this.PublishedHostPort == 0 ? """[{"NetworkSettings":{"Ports":{}}}]""" :
					JsonSerializer.Serialize(new[] { new { NetworkSettings = new { Ports = new Dictionary<string, object> { ["45000/tcp"] = new[] { new { HostIp = "0.0.0.0", HostPort = this.PublishedHostPort.ToString(CultureInfo.InvariantCulture) } } } } } });

			return Task.FromResult(new ProcessResult(0, output, ""));
		}
	}
}
