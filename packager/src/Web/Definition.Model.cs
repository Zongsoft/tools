/*
 *   _____                                ______
 *  /_   /  ____  ____  ____  _________  / __/ /_
 *    / /  / __ \/ __ \/ __ \/ ___/ __ \/ /_/ __/
 *   / /__/ /_/ / / / / /_/ /\_ \/ /_/ / __/ /_
 *  /____/\____/_/ /_/\__  /____/\____/_/  \__/
 *                   /____/
 *
 * Authors:
 *   钟峰(Popeye Zhong) <zongsoft@qq.com>
 *
 * Copyright (c) Zongsoft Studio. All rights reserved.
 * The above copyright notice and this permission notice shall be included in all
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 *
 *
 * Licensed under the MIT License (see LICENSE).
 */

using System;
using System.Numerics;
using System.Collections.Generic;

namespace Zongsoft.Tools.Packager.Web;

partial class Definition
{
	internal sealed record Model(IReadOnlyList<Site> Sites, IReadOnlyList<Diagnostic> Diagnostics);
	internal sealed record Site(string Name, IReadOnlyList<string> Hosts, IReadOnlyList<Binding> Bindings, Certificate Certificates, IReadOnlyList<Route> Routes, IReadOnlyList<RawDirective> Directives, Diagnostic.Location Source, Route Default = null);
	internal sealed record Route(string Name, string Path, MatchKind Match, ServerGroup Server, IReadOnlyList<Header> Headers, bool WebSocket, IReadOnlyList<RawDirective> Directives, Diagnostic.Location Source, BindingDocument.SiteRoute Mapping = null);
	internal sealed record ServerGroup(ServerKind Kind, IReadOnlyList<ServerGroup.Member> Members, ServerPolicy Policy, string Selector = null)
	{
		internal readonly record struct Member(string Name, Endpoint Endpoint, BigInteger Weight);
	}
	internal sealed record ServerPolicy(string Balance, BigInteger FailureCount, TimeSpan FailureTimeout, RetryPolicy Retry, AffinityPolicy Affinity, TlsPolicy Tls, HealthPolicy Health);
	internal sealed record RetryPolicy(IReadOnlyList<string> Conditions, BigInteger Count);
	internal sealed record AffinityPolicy(string Kind, string Cookie);
	internal sealed record TlsPolicy(bool Verify, ResourceReference? Trust, string Name);
	internal sealed record HealthPolicy(string Path, TimeSpan Interval, TimeSpan ConnectTimeout, TimeSpan SendTimeout, TimeSpan ReadTimeout, BigInteger FailureCount, BigInteger RecoveryCount, IReadOnlyList<int> Status, IReadOnlyList<Header> Headers);
	internal sealed record Certificate(ResourceReference File, ResourceReference Key);
	internal readonly record struct Binding(string Scheme, string Address, int Port);
	internal readonly record struct Endpoint(string Scheme, string Host, int Port, string Path = "/", string Protocol = "http1")
	{
		internal string Authority => $"{(this.Host.Contains(':') ? "[" + this.Host + "]" : this.Host)}:{this.Port}";
		internal string Address => $"{this.Scheme}://{this.Authority}";
	}

	internal readonly record struct Header(string Name, string Value, bool Expression, Diagnostic.Location Source);
	internal readonly record struct ResourceReference(string Path, bool Relative = false);
	internal readonly record struct RawDirective(string Name, string Suffix, string Value, Diagnostic.Location Source);

	internal enum MatchKind { Prefix, Exact, Regex }
	internal enum ServerKind { Application, Single, Pool }
	internal sealed record Rules(string Hoster);
}
