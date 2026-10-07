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
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.IO;
using System.Net.Sockets;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer;

internal sealed partial class RunContext
{
	private sealed class Endpoint(ServicePlan service, PortPlan port, int relayPort)
	{
		#region 公共属性
		public ServicePlan Service { get; } = service;
		public PortPlan Port { get; } = port;
		public int RelayPort { get; } = relayPort;
		public int HostPort { get; private set; }
		public bool IsWeb => this.ProbeTargets.Any();
		public string BindAddress => this.IsWeb ? "0.0.0.0" : "127.0.0.1";
		public string Address => this.ProbeTargets.Select(probe => this.GetUrl(probe.Scheme, probe.Host)).FirstOrDefault() ?? $"127.0.0.1:{this.HostPort}";
		public List<(string Application, string Site, string Address, string Redirect)> ProbeResults { get; } = [];
		public string ForwardTarget => this.Port.Address.Contains(':') ?
			$"TCP6:[{(this.Port.Address == "::" ? "::1" : this.Port.Address)}]:{this.Port.Host}" :
			$"TCP4:{(this.Port.Address == "0.0.0.0" ? "127.0.0.1" : this.Port.Address)}:{this.Port.Host}";
		#endregion

		#region 私有属性
		private IEnumerable<(string Application, string Site, string Scheme, string Host)> ProbeTargets =>
			this.Service.Web.SelectMany(site => site.Bindings
				.Where(binding => binding.Publication == this.Port.Name && binding.Address == "0.0.0.0")
				.SelectMany(binding => site.ProbeHosts.Select(host => (site.Application, $"{site.Application}({site.Name})", binding.Scheme, host)))
			).Distinct();
		#endregion

		#region 公共方法
		public static List<Endpoint> Create(DeliveryPlan plan)
		{
			var reserved = plan.Services.SelectMany(service => service.Ports).Select(port => port.Host).ToHashSet();
			var endpoints = new List<Endpoint>();
			var relayPort = 45000;

			foreach(var service in plan.Services)
			{
				foreach(var port in service.Ports.Where(port => port.Protocol == "tcp"))
				{
					while(relayPort <= 65535 && !reserved.Add(relayPort))
						relayPort++;

					if(relayPort > 65535)
						throw new ContainerizationException(2, Properties.Resources.Run_TooManyPorts_Message);

					endpoints.Add(new(service, port, relayPort++));
				}
			}

			return endpoints;
		}

		public static int GetAvailablePort(int preferredPort, bool isWeb = false)
		{
			using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

			try
			{
				socket.ExclusiveAddressUse = true;
				socket.Bind(new IPEndPoint(isWeb ? IPAddress.Any : IPAddress.Loopback, preferredPort));

				return preferredPort;
			}
			catch(SocketException) { return 0; }
		}

		public void Bind(JsonElement ports)
		{
			var mapping = ports.GetProperty($"{this.RelayPort}/tcp").EnumerateArray().Single(item => item.GetProperty("HostIp").GetString() == this.BindAddress);
			this.HostPort = int.Parse(mapping.GetProperty("HostPort").GetString(), CultureInfo.InvariantCulture);
		}

		public string[] GetLocalAddresses()
		{
			try
			{
				return NetworkInterface.GetAllNetworkInterfaces()
					.Where(network => network.OperationalStatus == OperationalStatus.Up)
					.SelectMany(network => network.GetIPProperties().UnicastAddresses)
					.Select(unicast => unicast.Address)
					.Where(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
					.Select(address => $"{address}:{this.HostPort}").Distinct().ToArray();
			}
			catch(NetworkInformationException) { return []; }
		}

		public async Task ProbeAsync(CancellationToken cancellation)
		{
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
			timeout.CancelAfter(TimeSpan.FromSeconds(15));

			using var handler = new SocketsHttpHandler
			{
				UseProxy = false,
				AllowAutoRedirect = false,

				ConnectCallback = async (_, token) =>
				{
					var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

					try
					{
						await socket.ConnectAsync(IPAddress.Loopback, this.HostPort, token);
						return new NetworkStream(socket, true);
					}
					catch { socket.Dispose(); throw; }
				},
			};

			Exception failure = null;
			using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };

			while(!timeout.IsCancellationRequested)
			{
				try
				{
					if(this.IsWeb)
					{
						this.ProbeResults.Clear();
						foreach(var probe in this.ProbeTargets)
						{
							var address = this.GetUrl(probe.Scheme, probe.Host);
							using var response = await client.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
							if((int)response.StatusCode >= 500)
								response.EnsureSuccessStatusCode();

							var location = response.Headers.Location;
							this.ProbeResults.Add((probe.Application, probe.Site, address, location == null ? null : new Uri(new Uri(address), location).AbsoluteUri));
						}
					}
					else
					{
						using var socket = new TcpClient();
						await socket.ConnectAsync(IPAddress.Loopback, this.HostPort, timeout.Token);
					}

					return;
				}
				catch(Exception exception) when(exception is HttpRequestException or SocketException or OperationCanceledException)
				{
					cancellation.ThrowIfCancellationRequested();
					failure = exception;

					if(timeout.IsCancellationRequested)
						break;

					try
					{
						await Task.Delay(300, timeout.Token);
					}
					catch(OperationCanceledException) when(!cancellation.IsCancellationRequested)
					{
						break;
					}
				}
			}

			throw new ContainerizationException(4, failure?.Message ?? Properties.Resources.Run_ProbeTimeout_Message, failure);
		}
		#endregion

		#region 私有方法
		private string GetUrl(string scheme, string host) => new UriBuilder(scheme, host, this.HostPort).Uri.AbsoluteUri;
		#endregion
	}
}
