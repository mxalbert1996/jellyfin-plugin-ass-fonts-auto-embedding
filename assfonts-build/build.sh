#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
base_image="debian:trixie"
host_arch="$(uname -m)"

cleanup_build_root() {
    rm -rf "$1/build" "$1/cache" "$1/home"
}

run_build() {
    local build_root="$1"
    local container_platform="$2"
    local pkg_config_path="$3"
    local host_triplet="$4"
    local target_triplet="$5"
    local apt_packages="$6"
    local configure_extra="$7"
    local install_dir="${build_root}/install"
    local cache_root="${build_root}/cache"
    local home_dir="${build_root}/home"
    local vcpkg_downloads="${cache_root}/vcpkg-downloads"
    local vcpkg_binary_cache="${cache_root}/vcpkg-binary-cache"
    local build_root_in_container="/work${build_root#${repo_root}}"
    local home_in_container="/work${home_dir#${repo_root}}"
    local downloads_in_container="/work${vcpkg_downloads#${repo_root}}"
    local binary_cache_in_container="/work${vcpkg_binary_cache#${repo_root}}"

    mkdir -p "${install_dir}" "${vcpkg_downloads}" "${vcpkg_binary_cache}" "${home_dir}"

    docker run --rm \
        --platform "${container_platform}" \
        -e DEBIAN_FRONTEND=noninteractive \
        -e HOME="${home_in_container}" \
        -e HOST_UID="$(id -u)" \
        -e HOST_GID="$(id -g)" \
        -e ASSFONTS_PKG_CONFIG_PATH="${pkg_config_path}" \
        -e ASSFONTS_VCPKG_HOST_TRIPLET="${host_triplet}" \
        -e ASSFONTS_VCPKG_TARGET_TRIPLET="${target_triplet}" \
        -e ASSFONTS_CONFIGURE_EXTRA="${configure_extra}" \
        -e ASSFONTS_BUILD_ROOT="${build_root_in_container}" \
        -e VCPKG_DOWNLOADS="${downloads_in_container}" \
        -e VCPKG_DEFAULT_BINARY_CACHE="${binary_cache_in_container}" \
        -v "${repo_root}:/work" \
        -v "${vcpkg_downloads}:${downloads_in_container}" \
        -v "${vcpkg_binary_cache}:${binary_cache_in_container}" \
        -w /work \
        "${base_image}" \
        bash -lc "set -euo pipefail; apt-get update; apt-get install -y --no-install-recommends ${apt_packages}; rm -rf /var/lib/apt/lists/*; setpriv --reuid=\"\${HOST_UID}\" --regid=\"\${HOST_GID}\" --clear-groups bash -lc 'set -euo pipefail; export PKG_CONFIG_PATH=\"\${ASSFONTS_PKG_CONFIG_PATH}\"; if [ ! -x /work/assfonts/vcpkg/vcpkg ]; then /work/assfonts/vcpkg/bootstrap-vcpkg.sh -disableMetrics; fi; cmake -S /work/assfonts-build -B \"\${ASSFONTS_BUILD_ROOT}/build\" -G Ninja -DCMAKE_BUILD_TYPE=Release -DCMAKE_INSTALL_PREFIX=\"\${ASSFONTS_BUILD_ROOT}/install\" \${ASSFONTS_CONFIGURE_EXTRA}; cmake --build \"\${ASSFONTS_BUILD_ROOT}/build\" --target libassfonts; cmake --install \"\${ASSFONTS_BUILD_ROOT}/build\"'" \
        | tee "${build_root}/build.log"

    cleanup_build_root "${build_root}"
}

if ! command -v docker >/dev/null 2>&1; then
    printf 'docker is required\n' >&2
    exit 1
fi

if [ ! -d "${repo_root}/assfonts-build" ]; then
    printf 'assfonts-build/ is missing\n' >&2
    exit 1
fi

if [ ! -f "${repo_root}/.gitmodules" ]; then
    printf '.gitmodules is missing; cannot verify submodules\n' >&2
    exit 1
fi

if [ ! -d "${repo_root}/.git" ] && [ ! -f "${repo_root}/.git" ]; then
    printf 'repository metadata is missing; run from a git checkout\n' >&2
    exit 1
fi

if [ ! -d "${repo_root}/assfonts/.git" ] && [ ! -f "${repo_root}/assfonts/.git" ]; then
    git -C "${repo_root}" submodule update --init --recursive assfonts
fi

if [ ! -f "${repo_root}/assfonts/CMakeLists.txt" ]; then
    printf 'assfonts submodule is not available\n' >&2
    exit 1
fi

if [ ! -d "${repo_root}/assfonts/vcpkg/.git" ] && [ ! -f "${repo_root}/assfonts/vcpkg/.git" ]; then
    git -C "${repo_root}" submodule update --init --recursive assfonts
fi

if [ ! -f "${repo_root}/assfonts/vcpkg/bootstrap-vcpkg.sh" ]; then
    printf 'assfonts/vcpkg is not available\n' >&2
    exit 1
fi

case "${host_arch}" in
    x86_64|amd64)
        run_build \
            "${repo_root}/build/assfonts-x86_64" \
            linux/amd64 \
            /usr/lib/x86_64-linux-gnu/pkgconfig \
            x64-linux-release \
            x64-linux-release \
            "build-essential ca-certificates cmake curl git ninja-build pkg-config python3 tar unzip util-linux zip" \
            "-DVCPKG_HOST_TRIPLET=x64-linux-release -DVCPKG_TARGET_TRIPLET=x64-linux-release"
        run_build \
            "${repo_root}/build/assfonts-arm64" \
            linux/amd64 \
            /usr/lib/aarch64-linux-gnu/pkgconfig \
            x64-linux-release \
            arm64-linux-release \
            "build-essential ca-certificates cmake curl g++-aarch64-linux-gnu gcc-aarch64-linux-gnu git libc6-dev-arm64-cross ninja-build pkg-config python3 tar unzip util-linux zip" \
            "-DCMAKE_EXPORT_COMPILE_COMMANDS=ON -DCMAKE_SYSTEM_NAME=Linux -DCMAKE_C_FLAGS=-fPIC -DCMAKE_CXX_FLAGS=-fPIC -DVCPKG_HOST_TRIPLET=x64-linux-release -DVCPKG_TARGET_TRIPLET=arm64-linux-release -DCMAKE_C_COMPILER=aarch64-linux-gnu-gcc -DCMAKE_CXX_COMPILER=aarch64-linux-gnu-g++ -DCMAKE_SYSROOT_COMPILE=/usr/aarch64-linux-gnu -DCMAKE_FIND_ROOT_PATH=/usr/lib/aarch64-linux-gnu -DCMAKE_FIND_ROOT_PATH_MODE_PROGRAM=NEVER -DCMAKE_FIND_ROOT_PATH_MODE_LIBRARY=ONLY -DCMAKE_FIND_ROOT_PATH_MODE_PACKAGE=ONLY -DCMAKE_FIND_ROOT_PATH_MODE_INCLUDE=BOTH -DCMAKE_CROSSCOMPILING=ON"
        ;;
    aarch64|arm64)
        run_build \
            "${repo_root}/build/assfonts-arm64" \
            linux/arm64 \
            /usr/lib/aarch64-linux-gnu/pkgconfig \
            arm64-linux-release \
            arm64-linux-release \
            "build-essential ca-certificates cmake curl git ninja-build pkg-config python3 tar unzip util-linux zip" \
            "-DVCPKG_HOST_TRIPLET=arm64-linux-release -DVCPKG_TARGET_TRIPLET=arm64-linux-release"
        run_build \
            "${repo_root}/build/assfonts-x86_64" \
            linux/arm64 \
            /usr/lib/x86_64-linux-gnu/pkgconfig \
            arm64-linux-release \
            x64-linux-release \
            "build-essential ca-certificates cmake curl g++-x86-64-linux-gnu gcc-x86-64-linux-gnu git libc6-dev-amd64-cross ninja-build pkg-config python3 tar unzip util-linux zip" \
            "-DCMAKE_EXPORT_COMPILE_COMMANDS=ON -DCMAKE_SYSTEM_NAME=Linux -DCMAKE_C_FLAGS=-fPIC -DCMAKE_CXX_FLAGS=-fPIC -DVCPKG_HOST_TRIPLET=arm64-linux-release -DVCPKG_TARGET_TRIPLET=x64-linux-release -DCMAKE_C_COMPILER=x86_64-linux-gnu-gcc -DCMAKE_CXX_COMPILER=x86_64-linux-gnu-g++ -DCMAKE_SYSROOT_COMPILE=/usr/x86_64-linux-gnu -DCMAKE_FIND_ROOT_PATH=/usr/lib/x86_64-linux-gnu -DCMAKE_FIND_ROOT_PATH_MODE_PROGRAM=NEVER -DCMAKE_FIND_ROOT_PATH_MODE_LIBRARY=ONLY -DCMAKE_FIND_ROOT_PATH_MODE_PACKAGE=ONLY -DCMAKE_FIND_ROOT_PATH_MODE_INCLUDE=BOTH -DCMAKE_CROSSCOMPILING=ON"
        ;;
    *)
        printf 'unsupported architecture: %s\n' "${host_arch}" >&2
        exit 1
        ;;
esac
