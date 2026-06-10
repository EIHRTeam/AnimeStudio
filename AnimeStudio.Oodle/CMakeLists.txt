cmake_minimum_required(VERSION 3.20)
project(AnimeStudioOoz LANGUAGES C CXX)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

option(OOZ_BUILD_BUN "Build Bun library and utilities" OFF)
option(OOZ_BUILD_EXE "Build ooz executable" OFF)
option(OOZ_BUILD_VALIDATE "Build ooz validator" OFF)

set(OOZ_SOURCES
    bitknit.cpp
    bits_rev_table.h
    compr_entropy.cpp
    compr_entropy.h
    compr_kraken.cpp
    compr_kraken.h
    compr_leviathan.cpp
    compr_leviathan.h
    compr_match_finder.cpp
    compr_match_finder.h
    compr_mermaid.cpp
    compr_mermaid.h
    compr_multiarray.cpp
    compr_tans.cpp
    compr_util.h
    compress.cpp
    compress.h
    kraken.cpp
    log_lookup.h
    lzna.cpp
    match_hasher.h
    qsort.h
    targetver.h
)

add_library(AnimeStudioOoz SHARED ${OOZ_SOURCES})

set_target_properties(AnimeStudioOoz PROPERTIES
    OUTPUT_NAME "AnimeStudio.Ooz"
    CXX_VISIBILITY_PRESET hidden
    VISIBILITY_INLINES_HIDDEN YES
)

target_compile_definitions(AnimeStudioOoz PUBLIC OOZ_DYNAMIC)
target_compile_definitions(AnimeStudioOoz PRIVATE OOZ_BUILD_DLL)

target_include_directories(AnimeStudioOoz PUBLIC simde)

if (OOZ_BUILD_VALIDATE OR OOZ_BUILD_BUN)
    if (UNIX)
        find_package(PkgConfig REQUIRED)
        pkg_check_modules(libsodium REQUIRED IMPORTED_TARGET libsodium)
    endif()
endif()

if (OOZ_BUILD_EXE)
    add_executable(ooz ${OOZ_SOURCES})
    target_include_directories(ooz PUBLIC simde)
endif()

if (OOZ_BUILD_VALIDATE)
    add_executable(ooz-validate ${OOZ_SOURCES} validate.cpp)
    target_compile_definitions(ooz-validate PUBLIC OOZ_DYNAMIC=0)
    target_compile_definitions(ooz-validate PRIVATE OOZ_BUILD_DLL=1)
    target_include_directories(ooz-validate PRIVATE simde)
    target_link_libraries(ooz-validate PRIVATE PkgConfig::libsodium)
endif()

if (OOZ_BUILD_BUN)
    add_library(bunutil STATIC
        "fnv.cpp" "fnv.h"
        "murmur.cpp" "murmur.h"
        "path_rep.cpp" "path_rep.h"
        "util.cpp" "util.h"
        "utf.cpp" "utf.h"
        )
    target_include_directories(bunutil PUBLIC ${CMAKE_CURRENT_SOURCE_DIR})
    if (UNIX)
        target_link_libraries(bunutil
            PUBLIC
                PkgConfig::libsodium
                "unistring"
        )
    endif()

    add_library(libbun SHARED  "bun.cpp" "bun.h")
    target_compile_definitions(libbun PUBLIC BUN_DYNAMIC)
    target_compile_definitions(libbun PRIVATE BUN_BUILD_DLL)
    target_include_directories(libbun INTERFACE ${CMAKE_CURRENT_SOURCE_DIR})
    target_link_libraries(libbun PUBLIC bunutil)
    if (UNIX)
        target_link_libraries(libbun PRIVATE "-lstdc++fs" dl)
    endif()

    add_subdirectory(libpoe)

    add_executable(bun_extract_file "bun_extract_file.cpp" "ggpk_vfs.cpp" "ggpk_vfs.h")
    target_link_libraries(bun_extract_file PRIVATE libbun libpoe)
    if (UNIX)
        target_link_libraries(bun_extract_file PRIVATE "-lstdc++fs")
    endif()
endif()
