#!/usr/bin/env bash

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
rid="${1:-}"
configuration="${CONFIGURATION:-Release}"
build_root="${BUILD_ROOT:-${repo_root}/build/native}"

if [[ -z "${rid}" ]]; then
    echo "Usage: $0 <linux-x64|osx-arm64>" >&2
    exit 2
fi

case "${rid}" in
    osx-arm64)
        if [[ "$(uname -s)" != "Darwin" || "$(uname -m)" != "arm64" ]]; then
            echo "osx-arm64 native assets must be built on an Apple Silicon macOS host." >&2
            exit 2
        fi
        : "${FBX_SDK_ROOT:=/Applications/Autodesk/FBX SDK/2020.3.9}"
        cmake_platform_args=(
            -DCMAKE_OSX_ARCHITECTURES=arm64
            -DCMAKE_OSX_DEPLOYMENT_TARGET=15.0
        )
        library_prefix="lib"
        library_suffix=".dylib"
        ;;
    linux-x64)
        if [[ "$(uname -s)" != "Linux" || "$(uname -m)" != "x86_64" ]]; then
            echo "linux-x64 native assets must be built on an x86_64 Linux host." >&2
            exit 2
        fi
        : "${FBX_SDK_ROOT:?Set FBX_SDK_ROOT to the extracted Autodesk FBX SDK 2020.3.9 directory.}"
        cmake_platform_args=()
        library_prefix="lib"
        library_suffix=".so"
        ;;
    *)
        echo "Unsupported RID: ${rid}" >&2
        exit 2
        ;;
esac

destination="${repo_root}/AnimeStudio.Libraries/runtimes/${rid}/native"
ooz_build="${build_root}/${rid}/ooz"
fbx_build="${build_root}/${rid}/fbx"

cmake -S "${repo_root}/AnimeStudio.Oodle" -B "${ooz_build}" \
    -DCMAKE_BUILD_TYPE="${configuration}" \
    -DOOZ_BUILD_BUN=OFF \
    -DOOZ_BUILD_EXE=OFF \
    -DOOZ_BUILD_VALIDATE=OFF \
    "${cmake_platform_args[@]}"
cmake --build "${ooz_build}" --config "${configuration}" --parallel

cmake -S "${repo_root}/AnimeStudio.FBXNative" -B "${fbx_build}" \
    -DCMAKE_BUILD_TYPE="${configuration}" \
    -DFBX_SDK_ROOT="${FBX_SDK_ROOT}" \
    "${cmake_platform_args[@]}"
cmake --build "${fbx_build}" --config "${configuration}" --parallel

mkdir -p "${destination}"
cmake -E copy_if_different \
    "${ooz_build}/${library_prefix}AnimeStudio.Ooz${library_suffix}" \
    "${destination}/${library_prefix}AnimeStudio.Ooz${library_suffix}"
cmake -E copy_if_different \
    "${fbx_build}/${library_prefix}AnimeStudio.FBXNative${library_suffix}" \
    "${destination}/${library_prefix}AnimeStudio.FBXNative${library_suffix}"

echo "Native assets written to ${destination}"
