cmake_minimum_required(VERSION 3.16)

set(ASSFONTS_SOURCE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../assfonts")
set(CMAKE_TOOLCHAIN_FILE "${ASSFONTS_SOURCE_DIR}/vcpkg/scripts/buildsystems/vcpkg.cmake"
    CACHE STRING "Vcpkg toolchain file")

project(assfonts-build LANGUAGES C CXX)

set(VERSION_MAJOR 0)
set(VERSION_MINOR 7)
set(VERSION_PATCH 3)
set(CXX_STD_VER cxx_std_17)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

include(GNUInstallDirs)

find_package(harfbuzz CONFIG REQUIRED)
find_package(Freetype REQUIRED)
find_package(nlohmann_json CONFIG REQUIRED)
find_package(Iconv REQUIRED)
find_package(fmt CONFIG REQUIRED)
find_package(ghc_filesystem CONFIG REQUIRED)
find_package(pcre2 CONFIG REQUIRED)
find_package(string-view-lite CONFIG REQUIRED)
find_package(Threads REQUIRED)

add_subdirectory(
    "${ASSFONTS_SOURCE_DIR}/3rdparty/compact_enc_det"
    "${CMAKE_CURRENT_BINARY_DIR}/3rdparty/compact_enc_det"
    EXCLUDE_FROM_ALL)

add_library(libasshdr STATIC
    "${ASSFONTS_SOURCE_DIR}/3rdparty/asshdr/lib/ass_recolorize.cc")

target_link_libraries(libasshdr PRIVATE PCRE2::8BIT)

target_include_directories(libasshdr
    PUBLIC "${ASSFONTS_SOURCE_DIR}/3rdparty/asshdr/include"
    PRIVATE "${ASSFONTS_SOURCE_DIR}/3rdparty/asshdr/lib")

target_compile_features(libasshdr PRIVATE ${CXX_STD_VER})

set_target_properties(libasshdr PROPERTIES PREFIX "")

set(LIBASSFONTS_SOURCES
    "${ASSFONTS_SOURCE_DIR}/lib/font_parser.cc"
    "${ASSFONTS_SOURCE_DIR}/lib/ass_parser.cc"
    "${ASSFONTS_SOURCE_DIR}/lib/font_subsetter.cc"
    "${ASSFONTS_SOURCE_DIR}/lib/ass_font_embedder.cc"
    "${ASSFONTS_SOURCE_DIR}/lib/ass_string.cc"
    "${ASSFONTS_SOURCE_DIR}/lib/ass_utf8.cc"
    "${ASSFONTS_SOURCE_DIR}/lib/ass_freetype.cc"
    "${ASSFONTS_SOURCE_DIR}/lib/assfonts.cc")

if(APPLE)
    list(APPEND LIBASSFONTS_SOURCES "${ASSFONTS_SOURCE_DIR}/lib/get_home_dir_macos.mm")
endif()

configure_file(
    "${ASSFONTS_SOURCE_DIR}/include/assfonts.h.in"
    "${CMAKE_CURRENT_BINARY_DIR}/include/assfonts.h")

add_library(libassfonts SHARED)

target_sources(libassfonts PRIVATE ${LIBASSFONTS_SOURCES})

target_link_libraries(libassfonts PRIVATE
    Threads::Threads
    harfbuzz::harfbuzz-subset
    Freetype::Freetype
    nlohmann_json::nlohmann_json
    Iconv::Iconv
    fmt::fmt
    ced
    libasshdr
    ghcFilesystem::ghc_filesystem
    nonstd::string-view-lite)

if(APPLE)
    target_link_options(libassfonts PRIVATE -framework Foundation)
endif()

target_include_directories(libassfonts
    PRIVATE "${ASSFONTS_SOURCE_DIR}/lib"
    PUBLIC "${CMAKE_CURRENT_BINARY_DIR}/include")

target_compile_features(libassfonts PRIVATE ${CXX_STD_VER})

set_target_properties(libassfonts PROPERTIES PREFIX "")

install(TARGETS libassfonts
    LIBRARY DESTINATION ${CMAKE_INSTALL_LIBDIR}
    ARCHIVE DESTINATION ${CMAKE_INSTALL_LIBDIR}
    RUNTIME DESTINATION ${CMAKE_INSTALL_BINDIR})

install(FILES "${CMAKE_CURRENT_BINARY_DIR}/include/assfonts.h"
    DESTINATION ${CMAKE_INSTALL_INCLUDEDIR})
