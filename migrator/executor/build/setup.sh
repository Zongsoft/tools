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
if [[ ! -d "$DOTNET_ROOT/sdk/11.0.100" ]]; then
	archive=/aot/dotnet-sdk-11.0.100-linux-x64.tar.gz
	curl --fail --location --retry 3 --output "$archive" \
		https://builds.dotnet.microsoft.com/dotnet/Sdk/11.0.100/dotnet-sdk-11.0.100-linux-x64.tar.gz
	printf '%s  %s\n' 1bec9cca2ec689b1e80245b84f2b53c9441d1aa6daccf2ceb27490768bffa95954fa585bd275def31069d874d543d9ba9e6591fe9ade2dbd9f34ec5c0fd0050c "$archive" | sha512sum --check
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
