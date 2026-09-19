#!/usr/bin/env bash
set -euo pipefail

# This script runs only inside the dedicated Rocky Linux 9 build container.
test "$(uname -m)" = x86_64
. /etc/os-release
test "$ID" = rocky
test "${VERSION_ID%%.*}" = 9

prerequisites=(clang lld llvm gcc gcc-c++ zlib-devel openssl-devel krb5-devel libicu-devel dnf-plugins-core cpio file findutils tar gzip)
if ! command -v curl >/dev/null; then
	dnf install -y curl
fi
if ! rpm -q "${prerequisites[@]}" >/dev/null; then
	dnf install -y "${prerequisites[@]}"
fi

# Pin an SDK whose Roslyn compiler can load Zongsoft.CodeAnalysis 1.1.0.
export DOTNET_ROOT=/aot/dotnet
export PATH="$DOTNET_ROOT:$PATH"
if [[ ! -d "$DOTNET_ROOT/sdk/10.0.401" ]]; then
	archive=/aot/dotnet-sdk-10.0.401-linux-x64.tar.gz
	curl --fail --location --retry 3 --output "$archive" \
		https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.401/dotnet-sdk-10.0.401-linux-x64.tar.gz
	printf '%s  %s\n' 51c8b999af9e8dd9998c9edc5944e19a90788862068acd38694e098889054ce8c23d4f0c5cccfa16bf187d044562359e5ee69a9f8ad0bbe913ba90311fbce25b "$archive" | sha512sum --check
	mkdir -p "$DOTNET_ROOT"
	tar -xzf "$archive" -C "$DOTNET_ROOT"
fi

# Extract target RPMs instead of executing ARM package installation scripts.
if [[ ! -f /aot/ready ]]; then
	mkdir -p /aot/sysroot-rpms /aot/sysroots/linux-arm64
	dnf download --resolve --alldeps --forcearch=aarch64 \
		--destdir=/aot/sysroot-rpms glibc-devel zlib-devel libstdc++-devel libgcc
	# GCC carries the target crtbegin/crtend objects needed by clang's linker.
	dnf download --forcearch=aarch64 --destdir=/aot/sysroot-rpms gcc
	for package in /aot/sysroot-rpms/*.rpm; do
		(cd /aot/sysroots/linux-arm64 && rpm2cpio "$package" | cpio -idmu --quiet)
	done
fi

dotnet --info
clang --version
touch /aot/ready
