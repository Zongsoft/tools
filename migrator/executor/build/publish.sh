#!/usr/bin/env bash
set -euo pipefail

rid="${1:?Specify linux-x64 or linux-arm64}"
configuration="${2:-Release}"
case "$rid" in
	linux-x64|linux-arm64) ;;
	*) printf 'Unsupported runtime: %s\n' "$rid" >&2; exit 2 ;;
esac

case "$configuration" in
	Debug|Release) ;;
	*) printf 'Unsupported configuration: %s\n' "$configuration" >&2; exit 2 ;;
esac

test -f /aot/ready
export DOTNET_ROOT=/aot/dotnet
export PATH="$DOTNET_ROOT:$PATH"
test -d "$DOTNET_ROOT/sdk/10.0.401"
repository="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
artifacts="/aot/artifacts/executor/$configuration/$rid"
output="$repository/executor/src/bin/$configuration/net10.0/$rid/publish"
evidence="$repository/executor/src/bin/$configuration/net10.0/$rid/logs"
mkdir -p "$evidence"
log="$evidence/publish-$(date -u +%Y%m%dT%H%M%SZ).log"

properties=(-p:ZongsoftGuidelinesSynchronization= -p:LinkerFlavor=lld -p:StripSymbols=true -p:TrimmerSingleWarn=false -p:IlcSingleWarn=false)
if [[ "$rid" = linux-arm64 ]]; then
	install -m 755 "$repository/executor/build/clang-arm64.sh" /aot/clang-arm64
	properties+=(-p:SysRoot=/aot/sysroots/linux-arm64 -p:CppCompilerAndLinker=/aot/clang-arm64)
fi

# Limit compiler parallelism to keep the build usable in small Podman machines.
export DOTNET_PROCESSOR_COUNT="${DOTNET_PROCESSOR_COUNT:-2}"
mkdir -p "$artifacts/publish"
find "$artifacts/publish" -mindepth 1 -maxdepth 1 -exec rm -rf -- {} +
dotnet publish "$repository/executor/src/Zongsoft.Tools.Migrator.Executor.csproj" \
	--configuration "$configuration" --runtime "$rid" --self-contained \
	--artifacts-path "$artifacts" --output "$artifacts/publish" \
	"${properties[@]}" 2>&1 | tee "$log"
cp "$log" "$evidence/publish.log"

binary="$artifacts/publish/Zongsoft.Tools.Migrator.Executor"
file "$binary" | tee "$evidence/elf.txt"
test -x "$binary"
test -s "$artifacts/publish/libduckdb.so"
test -s "$artifacts/publish/libe_sqlite3.so"
test -s "$artifacts/publish/zh-Hans/Zongsoft.Tools.Migrator.Executor.resources.dll"
architecture=x86-64
[[ "$rid" != linux-arm64 ]] || architecture=aarch64
: > "$evidence/dependencies.txt"
while IFS= read -r -d '' native; do
	file "$native" | grep -F "$architecture"
	printf '\n%s\n' "$native" >> "$evidence/dependencies.txt"
	readelf -h -d --version-info "$native" >> "$evidence/dependencies.txt"
done < <(find "$artifacts/publish" -type f \( -name 'Zongsoft.Tools.Migrator.Executor' -o -name '*.so' \) -print0)

# Publish must succeed before replacing this RID's complete publication.
mkdir -p "$output"
find "$output" -mindepth 1 -maxdepth 1 -exec rm -rf -- {} +
cp -a "$artifacts/publish/." "$output/"
symbols="${output%/publish}/symbols"
mkdir -p "$symbols"
while IFS= read -r -d '' symbol; do
	mv "$symbol" "$symbols/"
done < <(find "$output" -type f \( -name '*.dbg' -o -name '*.pdb' \) -print0)
chmod 755 "$output/Zongsoft.Tools.Migrator.Executor"
