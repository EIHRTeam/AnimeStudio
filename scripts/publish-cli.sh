#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project="${repo_root}/AnimeStudio.CLI/AnimeStudio.CLI.csproj"
configuration="Release"
rid=""
version=""
output_dir="${repo_root}/artifacts"

usage() {
    echo "Usage: $0 <win-x64|linux-x64|osx-arm64> [--version <version>] [--output-dir <path>]" >&2
}

if [[ $# -eq 0 ]]; then
    usage
    exit 2
fi

rid="$1"
shift

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
        *)
            echo "Unknown argument: $1" >&2
            usage
            exit 2
            ;;
    esac
done

case "${rid}" in
    win-x64|linux-x64|osx-arm64)
        ;;
    *)
        echo "Unsupported RID: ${rid}" >&2
        exit 2
        ;;
esac

if [[ -z "${version}" ]]; then
    version="$(
        dotnet msbuild "${project}" \
            -nologo \
            -getProperty:Version \
            -p:TargetFramework=net10.0 |
            awk 'NF { value=$0 } END { print value }'
    )"
fi

if [[ -z "${version}" || "${version}" == *"/"* || "${version}" == *"\\"* ]]; then
    echo "Invalid package version: ${version}" >&2
    exit 2
fi

mkdir -p "${output_dir}"
output_dir="$(cd "${output_dir}" && pwd)"
stage_dir="${output_dir}/publish/${rid}"
package_base="AnimeStudio.CLI-${version}-${rid}"

rm -rf "${stage_dir}"
mkdir -p "${stage_dir}"

dotnet publish "${project}" \
    --configuration "${configuration}" \
    --framework net10.0 \
    --runtime "${rid}" \
    --self-contained false \
    --output "${stage_dir}" \
    -p:Version="${version}"

case "${rid}" in
    win-x64)
        command -v zip >/dev/null 2>&1 || {
            echo "The zip command is required to package win-x64." >&2
            exit 1
        }
        archive="${output_dir}/${package_base}.zip"
        rm -f "${archive}"
        (
            cd "${stage_dir}"
            zip -q -r "${archive}" .
        )
        ;;
    linux-x64|osx-arm64)
        archive="${output_dir}/${package_base}.tar.gz"
        rm -f "${archive}"
        tar -czf "${archive}" -C "${stage_dir}" .
        ;;
esac

rm -rf "${stage_dir}"
echo "Created ${archive}"
