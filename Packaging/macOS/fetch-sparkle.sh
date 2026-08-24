#!/bin/bash
set -euo pipefail

script_directory="$(cd "$(dirname "$0")" && pwd)"
metadata_path="$script_directory/Sparkle.json"
output_path="${1:-}"

if [[ -z "$output_path" ]]; then
    echo "用法: $0 <Sparkle 发行归档输出路径>" >&2
    exit 2
fi

if [[ -e "$output_path" ]]; then
    echo "输出已存在，不会覆盖: $output_path" >&2
    exit 3
fi

archive_url="$(plutil -extract archiveUrl raw "$metadata_path")"
expected_sha256="$(plutil -extract archiveSha256 raw "$metadata_path")"
output_parent="$(dirname "$output_path")"
mkdir -p "$output_parent"
temporary_path="$(mktemp "$output_parent/.sparkle-download.XXXXXX")"
trap 'rm -f "$temporary_path"' EXIT

curl --fail --location --retry 3 --output "$temporary_path" "$archive_url"
actual_sha256="$(shasum -a 256 "$temporary_path" | awk '{print $1}')"
if [[ "$actual_sha256" != "$expected_sha256" ]]; then
    echo "Sparkle 归档 SHA-256 不匹配。" >&2
    exit 4
fi

mv "$temporary_path" "$output_path"
echo "已获取并验证 Sparkle: $output_path"
