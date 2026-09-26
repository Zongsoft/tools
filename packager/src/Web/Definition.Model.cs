/*
 *   _____                                ______
 *  /_   /  ____  ____  ____  _________  / __/ /_
 *    / /  / __ \/ __ \/ __ \/ ___/ __ \/ /_/ __/
 *   / /__/ /_/ / / / / /_/ /\_ \/ /_/ / __/ /_
 *  /____/\____/_/ /_/\__  /____/\____/_/  \__/
 *                   /____/
 *
 * Authors:
 *   钟峰(Popeye Zhong) <zongsoft@gmail.com>
 *
 * The MIT License (MIT)
 *
 * Copyright (C) 2020-2026 Zongsoft Corporation <http://www.zongsoft.com>
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
using System.Numerics;
using System.Collections.Generic;

namespace Zongsoft.Tools.Packager.Web;

partial class Definition
{
	internal sealed record Model(IReadOnlyList<Site> Sites, IReadOnlyList<Diagnostic> Diagnostics);
	internal sealed record Site(string Name, IReadOnlyList<string> Hosts, IReadOnlyList<Binding> Bindings, Certificate Certificates, IReadOnlyList<Route> Routes, IReadOnlyList<RawDirective> Directives, Diagnostic.Location Source);
	internal sealed record Route(string Name, string Path, MatchKind Match, ServerGroup Server, IReadOnlyList<Header> Headers, bool WebSocket, IReadOnlyList<RawDirective> Directives, Diagnostic.Location Source);
	internal sealed record ServerGroup(ServerKind Kind, IReadOnlyList<ServerMember> Members, ServerPolicy Policy);
	internal sealed record ServerPolicy(string Balance, BigInteger FailureCount, TimeSpan FailureTimeout, RetryPolicy Retry, AffinityPolicy Affinity, TlsPolicy Tls, HealthPolicy Health);
	internal sealed record RetryPolicy(IReadOnlyList<string> Conditions, BigInteger Count);
	internal sealed record AffinityPolicy(string Kind, string Cookie);
	internal sealed record TlsPolicy(bool Verify, ResourceReference? Trust, string Name);
	internal sealed record HealthPolicy(string Path, TimeSpan Interval, TimeSpan ConnectTimeout, TimeSpan SendTimeout, TimeSpan ReadTimeout, BigInteger FailureCount, BigInteger RecoveryCount, IReadOnlyList<int> Status, IReadOnlyList<Header> Headers);
	internal sealed record Certificate(ResourceReference File, ResourceReference Key);
	internal readonly record struct Binding(string Scheme, string Address, int Port);
	internal readonly record struct Endpoint(string Scheme, string Host, int Port)
	{
		internal string Authority => $"{(this.Host.Contains(':') ? "[" + this.Host + "]" : this.Host)}:{this.Port}";
		internal string Address => $"{this.Scheme}://{this.Authority}";
	}

	internal readonly record struct ServerMember(string Name, Endpoint Endpoint, BigInteger Weight);
	internal readonly record struct Header(string Name, string Value, bool Expression, Diagnostic.Location Source);
	internal readonly record struct ResourceReference(string Path, bool Relative = false);
	internal readonly record struct RawDirective(string Name, string Suffix, string Value, Diagnostic.Location Source);

	internal enum MatchKind { Prefix, Exact, Regex }
	internal enum ServerKind { Application, Single, Pool }
	internal sealed record Rules(string Hoster);
}
