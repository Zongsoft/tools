#!/usr/bin/env bash
set -euo pipefail

# Override Rocky's host-only clang configuration during cross linking.
exec clang --no-default-config \
	--gcc-install-dir=/aot/sysroots/linux-arm64/usr/lib/gcc/aarch64-redhat-linux/11 "$@"
