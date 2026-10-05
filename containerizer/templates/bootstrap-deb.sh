#!/bin/sh
set -eu
. /etc/os-release
case "$ID:$VERSION_ID" in
	ubuntu:22.04|debian:12|debian:13) ;;
	*) printf 'Unsupported resolver distribution\n' >&2; exit 3 ;;
esac
apt-get update
apt-get install -y --no-install-recommends ca-certificates curl gnupg
# Lock HTTPS URLs for later cache imports and online acquisition.
find /etc/apt -type f \( -name '*.list' -o -name '*.sources' \) -exec sed -i 's|http://archive.ubuntu.com/ubuntu|https://archive.ubuntu.com/ubuntu|g;s|http://security.ubuntu.com/ubuntu|https://security.ubuntu.com/ubuntu|g;s|http://deb.debian.org|https://deb.debian.org|g' {} +
install -m 0755 -d /etc/apt/keyrings
curl -fsSL "https://download.docker.com/linux/$ID/gpg" -o /etc/apt/keyrings/docker.asc
printf 'deb [arch=%s signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/%s %s stable\n' "$(dpkg --print-architecture)" "$ID" "$VERSION_CODENAME" > /etc/apt/sources.list.d/docker.list
apt-get update
mkdir -p /delivery /delivery/partial
icu="$(apt-cache pkgnames | awk '/^libicu[0-9]+$/ { print }' | sort -u)"
ssl="$(apt-cache pkgnames | awk '/^libssl3(t64)?$/ { print }' | sort -u)"
test "$(printf '%s\n' "$icu" | wc -w)" -eq 1
test "$(printf '%s\n' "$ssl" | wc -w)" -eq 1
# An empty status database includes dependencies already installed in the resolver image.
apt-get -y --download-only --reinstall --no-install-recommends -o Dir::State::status=/dev/null -o Dir::Cache::archives=/delivery install docker-ce docker-ce-cli containerd.io docker-compose-plugin ca-certificates libc6 libgcc-s1 libstdc++6 libgssapi-krb5-2 zlib1g "$icu" "$ssl" tar gzip coreutils util-linux
apt-get -y --print-uris --reinstall --no-install-recommends -o Dir::State::status=/dev/null -o Dir::Cache::archives=/tmp/uris install docker-ce docker-ce-cli containerd.io docker-compose-plugin ca-certificates libc6 libgcc-s1 libstdc++6 libgssapi-krb5-2 zlib1g "$icu" "$ssl" tar gzip coreutils util-linux > /delivery/uris.txt
: > /delivery/metadata.tsv
for package in /delivery/*.deb; do
	filename="${package##*/}"
	url="$(awk -v file="$filename" '$2 == file { gsub(/\047/, "", $1); print $1; exit }' /delivery/uris.txt)"
	test -n "$url"
	name="$(dpkg-deb -f "$package" Package)"
	version="$(dpkg-deb -f "$package" Version)"
	architecture="$(dpkg-deb -f "$package" Architecture)"
	depends="$(dpkg-deb -f "$package" Pre-Depends Depends | tr '\n\t' '  ')"
	printf '%s\t%s\t%s\t%s\t%s\t%s\n' "$filename" "$name" "$version" "$architecture" "$url" "$depends" >> /delivery/metadata.tsv
done
rm -f /delivery/lock
rmdir /delivery/partial
