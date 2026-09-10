#!/usr/bin/env bash
set -euo pipefail

# This script runs only inside the dedicated Rocky Linux 9 build container.
test "$(uname -m)" = x86_64
. /etc/os-release
test "$ID" = rocky
test "${VERSION_ID%%.*}" = 9

dnf install -y dotnet-sdk-10.0 clang lld llvm gcc gcc-c++ zlib-devel \
	openssl-devel krb5-devel libicu-devel dnf-plugins-core cpio file findutils tar gzip

# Extract target RPMs instead of executing ARM package installation scripts.
mkdir -p /aot/sysroot-rpms /aot/sysroots/linux-arm64
dnf download --resolve --alldeps --forcearch=aarch64 \
	--destdir=/aot/sysroot-rpms glibc-devel zlib-devel libstdc++-devel libgcc
# GCC carries the target crtbegin/crtend objects needed by clang's linker.
dnf download --forcearch=aarch64 --destdir=/aot/sysroot-rpms gcc
for package in /aot/sysroot-rpms/*.rpm; do
	(cd /aot/sysroots/linux-arm64 && rpm2cpio "$package" | cpio -idmu --quiet)
done

dotnet --info
clang --version
touch /aot/ready
