#!/bin/bash
set -euo pipefail

script_directory="$(cd "$(dirname "$0")" && pwd)"
repository_root="$(cd "$script_directory/../.." && pwd)"
metadata_path="$script_directory/Sparkle.json"
mac_ui_project="$repository_root/Apps/Shigure.MacUI/Shigure.MacUI.csproj"
archives_directory="${1:-}"
output_name="${2:-appcast.xml}"
sparkle_archive="${SHIGURE_SPARKLE_ARCHIVE:-}"
keychain_account="${SHIGURE_SPARKLE_KEYCHAIN_ACCOUNT:-}"
download_url_prefix="${SHIGURE_SPARKLE_DOWNLOAD_URL_PREFIX:-}"

if [[ -z "$archives_directory" || ! -d "$archives_directory" ]]; then
    echo "用法: $0 <已公证更新归档目录> [appcast 文件名]" >&2
    exit 2
fi

if [[ "$output_name" == */* || "$output_name" != *.xml ]]; then
    echo "appcast 文件名必须是不含路径的 .xml 文件名: $output_name" >&2
    exit 3
fi

if [[ -z "$sparkle_archive" || ! -f "$sparkle_archive" ]]; then
    echo "SHIGURE_SPARKLE_ARCHIVE 必须指向锁定的 Sparkle 发行归档。" >&2
    exit 4
fi

if [[ -z "$keychain_account" ]]; then
    echo "SHIGURE_SPARKLE_KEYCHAIN_ACCOUNT 必须指定 EdDSA 私钥的钥匙串 account。" >&2
    exit 5
fi

if [[ "$download_url_prefix" != https://* ]]; then
    echo "SHIGURE_SPARKLE_DOWNLOAD_URL_PREFIX 必须是 HTTPS URL。" >&2
    exit 6
fi

expected_sha256="$(plutil -extract archiveSha256 raw "$metadata_path")"
actual_sha256="$(shasum -a 256 "$sparkle_archive" | awk '{print $1}')"
if [[ "$actual_sha256" != "$expected_sha256" ]]; then
    echo "Sparkle 归档 SHA-256 不匹配。" >&2
    exit 7
fi

minimum_update_version="$(dotnet msbuild "$mac_ui_project" -nologo -getProperty:ShigureMinimumUpdateBundleVersion)"
bundle_version="$(dotnet msbuild "$mac_ui_project" -nologo -getProperty:ShigureBundleVersion)"
maximum_deltas="$(plutil -extract maximumDeltas raw "$metadata_path")"
if [[ ! "$minimum_update_version" =~ ^([1-9][0-9]{0,3})\.(0|[1-9][0-9]?)\.(0|[1-9][0-9]?)$ ]]; then
    echo "ShigureMinimumUpdateBundleVersion 格式无效: $minimum_update_version" >&2
    exit 8
fi
if [[ "$maximum_deltas" != "0" ]]; then
    echo "首个 P3 发布必须禁用 delta 更新。" >&2
    exit 8
fi

IFS=. read -r minimum_major minimum_minor minimum_build <<<"$minimum_update_version"
IFS=. read -r bundle_major bundle_minor bundle_build <<<"$bundle_version"
minimum_order="$((minimum_major * 10000 + minimum_minor * 100 + minimum_build))"
bundle_order="$((bundle_major * 10000 + bundle_minor * 100 + bundle_build))"
if (( minimum_order > bundle_order )); then
    echo "最低更新版本不能高于当前 bundle 版本: $minimum_update_version > $bundle_version" >&2
    exit 8
fi

working_directory="$(mktemp -d "$archives_directory/.sparkle-tools.XXXXXX")"
trap 'rm -rf "$working_directory"' EXIT
tar -xf "$sparkle_archive" -C "$working_directory" ./bin/generate_appcast
generate_appcast="$working_directory/bin/generate_appcast"

"$generate_appcast" \
    --account "$keychain_account" \
    --download-url-prefix "$download_url_prefix" \
    --minimum-update-version "$minimum_update_version" \
    --maximum-deltas "$maximum_deltas" \
    -o "$output_name" \
    "$archives_directory"

echo "已生成签名 appcast: $archives_directory/$output_name"
