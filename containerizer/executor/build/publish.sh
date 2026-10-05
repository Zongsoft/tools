#!/usr/bin/env bash
set -euo pipefail
rid="${1:?Specify linux-x64 or linux-arm64}"
configuration="${2:-Release}"
case "$rid" in linux-x64|linux-arm64) ;; *) exit 2 ;; esac
case "$configuration" in Debug|Release) ;; *) exit 2 ;; esac
test -f /aot/ready
export DOTNET_ROOT=/aot/dotnet
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_PROCESSOR_COUNT="${DOTNET_PROCESSOR_COUNT:-2}"
repository="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
output="$repository/executor/src/bin/$configuration/net10.0/$rid/publish"
logs="$repository/executor/src/bin/$configuration/net10.0/$rid/logs"
mkdir -p "$output" "$logs"
properties=(-p:ZongsoftGuidelinesSynchronization= -p:ZongsoftCodeStyleStrict=true -p:LinkerFlavor=lld -p:StripSymbols=true -p:TrimmerSingleWarn=false -p:IlcSingleWarn=false)
if [[ "$rid" = linux-arm64 ]]; then
	install -m 755 "$repository/executor/build/clang-arm64.sh" /aot/containerizer-clang-arm64
	properties+=(-p:SysRoot=/aot/sysroots/linux-arm64 -p:CppCompilerAndLinker=/aot/containerizer-clang-arm64)
fi
dotnet publish "$repository/executor/src/Zongsoft.Tools.Containerizer.Executor.csproj" \
	-c "$configuration" -r "$rid" --self-contained --artifacts-path "/aot/artifacts/containerizer/$configuration/$rid" \
	-o "$output" "${properties[@]}" 2>&1 | tee "$logs/publish.log"
test -x "$output/containerizer"
file "$output/containerizer" | tee "$logs/elf.txt"
readelf -h -d --version-info "$output/containerizer" > "$logs/dependencies.txt"
if [[ "$rid" = linux-x64 ]]; then
	"$output/containerizer" --protocol
fi
mkdir -p "${output%/publish}/symbols"
if [[ -f "$output/containerizer.dbg" ]]; then
	mv "$output/containerizer.dbg" "${output%/publish}/symbols/"
fi
