#!/bin/sh
set -eu
. /etc/os-release
case "$ID:$VERSION_ID" in
	rocky:9*|almalinux:9*) repository=centos ;;
	rhel:9*) repository=rhel ;;
	*) printf 'A matching entitled resolver is required for this distribution\n' >&2; exit 3 ;;
esac
dnf install -y dnf-plugins-core ca-certificates curl
dnf config-manager --add-repo "https://download.docker.com/linux/$repository/docker-ce.repo"
mkdir -p /delivery
dnf download --resolve --alldeps --destdir=/delivery docker-ce docker-ce-cli containerd.io docker-compose-plugin glibc libgcc libstdc++ libicu krb5-libs zlib openssl-libs tar gzip coreutils util-linux
: > /delivery/metadata.tsv
for package in /delivery/*.rpm; do
	filename="${package##*/}"
	name="$(rpm -qp --qf '%{NAME}' "$package")"
	version="$(rpm -qp --qf '%{VERSION}-%{RELEASE}' "$package")"
	architecture="$(rpm -qp --qf '%{ARCH}' "$package")"
	url="$(dnf -q repoquery --location "$name-$version.$architecture" | head -n 1)"
	test -n "$url"
	depends="$(rpm -qp --requires "$package" | tr '\n\t' '; ')"
	printf '%s\t%s\t%s\t%s\t%s\t%s\n' "$filename" "$name" "$version" "$architecture" "$url" "$depends" >> /delivery/metadata.tsv
done
