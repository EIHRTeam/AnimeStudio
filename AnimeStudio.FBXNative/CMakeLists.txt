cmake_minimum_required(VERSION 3.20)
project(AnimeStudioFBXNative LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

set(FBX_SDK_ROOT "" CACHE PATH "Path to the Autodesk FBX SDK installation")
if(NOT FBX_SDK_ROOT)
    message(FATAL_ERROR "FBX_SDK_ROOT must point to an Autodesk FBX SDK installation")
endif()

set(FBX_NATIVE_SOURCES
    api.cpp
    asfbx_anim_context.cpp
    asfbx_context.cpp
    asfbx_morph_context.cpp
    asfbx_skin_context.cpp
    utils.cpp
)

add_library(AnimeStudioFBXNative SHARED ${FBX_NATIVE_SOURCES})
set_target_properties(AnimeStudioFBXNative PROPERTIES
    OUTPUT_NAME "AnimeStudio.FBXNative"
    CXX_VISIBILITY_PRESET hidden
    VISIBILITY_INLINES_HIDDEN YES
)

target_compile_definitions(AnimeStudioFBXNative PRIVATE _AS_DLL)
target_include_directories(AnimeStudioFBXNative PRIVATE "${FBX_SDK_ROOT}/include")

if(MSVC)
    set_property(TARGET AnimeStudioFBXNative PROPERTY
        MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()

set(FBX_LIBRARY_CANDIDATES)
if(WIN32)
    if(CMAKE_SIZEOF_VOID_P EQUAL 8)
        set(FBX_ARCH x64)
    else()
        set(FBX_ARCH x86)
    endif()

    list(APPEND FBX_LIBRARY_CANDIDATES
        "${FBX_SDK_ROOT}/lib/${FBX_ARCH}/release/libfbxsdk-mt.lib"
        "${FBX_SDK_ROOT}/lib/vs2022/${FBX_ARCH}/release/libfbxsdk-mt.lib"
        "${FBX_SDK_ROOT}/lib/vs2019/${FBX_ARCH}/release/libfbxsdk-mt.lib"
    )
elseif(APPLE)
    list(APPEND FBX_LIBRARY_CANDIDATES
        "${FBX_SDK_ROOT}/lib/clang/release/libfbxsdk.a"
        "${FBX_SDK_ROOT}/lib/legacy/release/libfbxsdk.a"
    )
else()
    list(APPEND FBX_LIBRARY_CANDIDATES
        "${FBX_SDK_ROOT}/lib/gcc/x64/release/libfbxsdk.a"
        "${FBX_SDK_ROOT}/lib/release/libfbxsdk.a"
    )
endif()

foreach(FBX_LIBRARY_CANDIDATE IN LISTS FBX_LIBRARY_CANDIDATES)
    if(EXISTS "${FBX_LIBRARY_CANDIDATE}")
        set(FBX_LIBRARY "${FBX_LIBRARY_CANDIDATE}")
        break()
    endif()
endforeach()

if(NOT FBX_LIBRARY)
    message(FATAL_ERROR
        "Could not find a static FBX SDK library under ${FBX_SDK_ROOT}. "
        "Checked: ${FBX_LIBRARY_CANDIDATES}")
endif()

target_link_libraries(AnimeStudioFBXNative PRIVATE "${FBX_LIBRARY}")

if(WIN32)
    get_filename_component(FBX_LIBRARY_DIR "${FBX_LIBRARY}" DIRECTORY)
    target_link_libraries(AnimeStudioFBXNative PRIVATE
        "${FBX_LIBRARY_DIR}/libxml2-mt.lib"
        "${FBX_LIBRARY_DIR}/zlib-mt.lib"
        wininet
    )
elseif(APPLE)
    find_package(Iconv REQUIRED)
    find_package(LibXml2 REQUIRED)
    find_package(ZLIB REQUIRED)
    target_link_libraries(AnimeStudioFBXNative PRIVATE
        Iconv::Iconv
        LibXml2::LibXml2
        ZLIB::ZLIB
        "-framework CoreServices"
        "-framework CoreFoundation"
        "-framework SystemConfiguration"
    )
else()
    find_package(LibXml2 REQUIRED)
    find_package(Threads REQUIRED)
    find_package(ZLIB REQUIRED)
    target_link_libraries(AnimeStudioFBXNative PRIVATE
        LibXml2::LibXml2
        Threads::Threads
        ZLIB::ZLIB
        dl
    )
endif()
