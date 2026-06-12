#!/usr/bin/env bash

set -euo pipefail

usage() {
    echo "Usage: $0 <input.tar.gz> <version> <output_dir>" >&2
    echo "" >&2
    echo "  input.tar.gz   Path to the AnimeStudio CLI linux-x64 tarball" >&2
    echo "  version        Semantic version (e.g. 1.2.3). Dpkg stores only" >&2
    echo "                 the leading numeric segment (1.0.0-CI becomes 1.0.0)." >&2
    echo "  output_dir     Directory where the .deb will be written" >&2
    exit 2
}

if [[ $# -ne 3 ]]; then
    usage
fi

input_tarball="$1"
raw_version="$2"
output_dir="$3"

# ---- Validate inputs ----

if [[ ! -f "${input_tarball}" ]]; then
    echo "Error: tarball not found: ${input_tarball}" >&2
    exit 1
fi

if [[ -z "${raw_version}" ]]; then
    echo "Error: version must not be empty" >&2
    exit 1
fi

# Debian uses only the numeric segment before the first hyphen as the package version.
# Examples: 1.2.3 -> 1.2.3, 1.0.0-CI -> 1.0.0, 2.0.0-beta.1 -> 2.0.0
deb_version=$(echo "${raw_version}" | sed -E 's/^([0-9]+(\.[0-9]+)*).*/\1/')

if [[ ! "${deb_version}" =~ ^[0-9]+(\.[0-9]+)+$ ]]; then
    echo "Error: cannot derive a valid Debian version from '${raw_version}'" >&2
    exit 1
fi

mkdir -p "${output_dir}"
output_dir="$(cd "${output_dir}" && pwd)"

echo "Input tarball : ${input_tarball}"
echo "Raw version   : ${raw_version}"
echo "Debian version: ${deb_version}"
echo "Output dir    : ${output_dir}"

# ---- Temp workspace ----

workdir="$(mktemp -d)"
trap 'rm -rf "${workdir}"' EXIT

# Extract tarball
mkdir -p "${workdir}/contents"
tar -xzf "${input_tarball}" -C "${workdir}/contents"

# ---- Build Debian package directory tree ----

pkg_root="${workdir}/deb-root"
mkdir -p "${pkg_root}/DEBIAN"
mkdir -p "${pkg_root}/usr/bin"
mkdir -p "${pkg_root}/usr/lib/anime-studio"
mkdir -p "${pkg_root}/usr/share/doc/anime-studio"

# Move all files to /usr/lib/anime-studio/, then relocate docs
mv "${workdir}/contents"/* "${pkg_root}/usr/lib/anime-studio/"

# Documentation files go to /usr/share/doc/anime-studio/
doc_dir="${pkg_root}/usr/share/doc/anime-studio"

if [[ -f "${pkg_root}/usr/lib/anime-studio/LICENSE" ]]; then
    cp "${pkg_root}/usr/lib/anime-studio/LICENSE" "${doc_dir}/copyright"
    rm -f "${pkg_root}/usr/lib/anime-studio/LICENSE"
fi

if [[ -f "${pkg_root}/usr/lib/anime-studio/THIRD_PARTY_NOTICES.md" ]]; then
    mv "${pkg_root}/usr/lib/anime-studio/THIRD_PARTY_NOTICES.md" "${doc_dir}/"
fi

# ---- Create wrapper script ----

cat > "${pkg_root}/usr/bin/anime-studio" <<'SCRIPT'
#!/bin/sh
export MALLOC_ARENA_MAX="${MALLOC_ARENA_MAX:-2}"
export MALLOC_TRIM_THRESHOLD_="${MALLOC_TRIM_THRESHOLD_:-131072}"
export DOTNET_GCConserveMemory="${DOTNET_GCConserveMemory:-5}"
exec dotnet /usr/lib/anime-studio/AnimeStudio.CLI.dll "$@"
SCRIPT
chmod 755 "${pkg_root}/usr/bin/anime-studio"

# ---- Symlink for alias 'anis' ----

ln -s /usr/bin/anime-studio "${pkg_root}/usr/bin/anis"

# ---- DEBIAN/conffiles ----

echo "/usr/lib/anime-studio/appsettings.json" > "${pkg_root}/DEBIAN/conffiles"

# ---- DEBIAN/control ----

cat > "${pkg_root}/DEBIAN/control" <<CTRL
Package: anime-studio-cli
Version: ${deb_version}
Section: utils
Priority: optional
Architecture: amd64
Depends: dotnet-sdk-10.0 | dotnet-runtime-10.0
Maintainer: EIHRTeam
Description: A fork of the original Anime Studio, with a focus on
 improving support for Linux CI/CD and macOS environments.
CTRL

# ---- Set permissions ----

find "${pkg_root}/usr/lib/anime-studio" -type f -exec chmod 644 {} +
find "${pkg_root}/usr/lib/anime-studio" -type d -exec chmod 755 {} +

# ELF AppHost must be executable
if [[ -f "${pkg_root}/usr/lib/anime-studio/AnimeStudio.CLI" ]]; then
    chmod 755 "${pkg_root}/usr/lib/anime-studio/AnimeStudio.CLI"
fi

# ---- Build the .deb ----

deb_name="anime-studio-cli_${deb_version}_amd64.deb"
dpkg-deb --build "${pkg_root}" "${output_dir}/${deb_name}"

echo ""
echo "Created ${output_dir}/${deb_name}"
