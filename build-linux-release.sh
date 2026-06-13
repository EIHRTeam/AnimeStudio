#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
version=""
output_dir="${repo_root}/artifacts"
build_deb=false

usage() {
    cat >&2 <<'EOF'
Usage: ./build-linux-release.sh [options]

Options:
  --version <version>    Package version. Defaults to the project version.
  --output-dir <path>    Output directory. Defaults to ./artifacts.
  --deb                  Also build an amd64 Debian package.
  -h, --help             Show this help.
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --version)
            [[ $# -ge 2 ]] || { usage; exit 2; }
            version="$2"
            shift 2
            ;;
        --output-dir)
            [[ $# -ge 2 ]] || { usage; exit 2; }
            output_dir="$2"
            shift 2
            ;;
        --deb)
            build_deb=true
            shift
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            echo "Unknown argument: $1" >&2
            usage
            exit 2
            ;;
    esac
done

if [[ "$(uname -s)" != "Linux" || "$(uname -m)" != "x86_64" ]]; then
    echo "This release script must run on Linux x86_64." >&2
    exit 1
fi

for command_name in dotnet tar sha256sum ldd; do
    command -v "${command_name}" >/dev/null 2>&1 || {
        echo "Required command is missing: ${command_name}" >&2
        exit 1
    }
done

sdk_version="$(dotnet --version)"
if [[ "${sdk_version}" != 10.* ]]; then
    echo "The .NET 10 SDK is required; found ${sdk_version}." >&2
    exit 1
fi

if [[ -z "${version}" ]]; then
    version="$(
        dotnet msbuild "${repo_root}/AnimeStudio.CLI/AnimeStudio.CLI.csproj" \
            -nologo \
            -getProperty:Version \
            -p:TargetFramework=net10.0 |
            awk 'NF { value=$0 } END { print value }'
    )"
fi

mkdir -p "${output_dir}"
output_dir="$(cd "${output_dir}" && pwd)"

dotnet restore "${repo_root}/AnimeStudio.sln"
dotnet build "${repo_root}/AnimeStudio.sln" \
    --configuration Release \
    --no-restore
dotnet run \
    --project "${repo_root}/scripts/AnimeStudio.Core.Smoke" \
    --configuration Release \
    --no-build

"${repo_root}/scripts/publish-cli.sh" linux-x64 \
    --version "${version}" \
    --output-dir "${output_dir}"

archive="${output_dir}/AnimeStudio.CLI-${version}-linux-x64.tar.gz"
if [[ ! -f "${archive}" ]]; then
    echo "Release archive was not created: ${archive}" >&2
    exit 1
fi

verify_dir="$(mktemp -d)"
trap 'rm -rf "${verify_dir}"' EXIT
tar -xzf "${archive}" -C "${verify_dir}"

required_files=(
    AnimeStudio.CLI
    AnimeStudio.CLI.dll
    appsettings.json
    libAnimeStudio.FBXNative.so
    libAnimeStudio.Ooz.so
    libTexture2DDecoderNative.so
    libfmod.so
)

for required_file in "${required_files[@]}"; do
    if [[ ! -f "${verify_dir}/${required_file}" ]]; then
        echo "Linux release is missing ${required_file}." >&2
        exit 1
    fi
done

if ldd "${verify_dir}/libAnimeStudio.FBXNative.so" | grep -q "not found"; then
    echo "libAnimeStudio.FBXNative.so has unresolved runtime dependencies:" >&2
    ldd "${verify_dir}/libAnimeStudio.FBXNative.so" >&2
    exit 1
fi

dotnet run \
    --project "${repo_root}/scripts/AnimeStudio.CLI.Smoke" \
    --configuration Release \
    --no-build \
    -- "${verify_dir}" linux-x64

if "${build_deb}"; then
    command -v dpkg-deb >/dev/null 2>&1 || {
        echo "The --deb option requires dpkg-deb." >&2
        exit 1
    }
    "${repo_root}/scripts/package-deb.sh" "${archive}" "${version}" "${output_dir}"
fi

echo
sha256sum "${archive}"
echo "Linux release created: ${archive}"
