#!/bin/bash
set -euo pipefail

script_directory="$(cd "$(dirname "$0")" && pwd)"
repository_root="$(cd "$script_directory/../.." && pwd)"
mac_ui_project="$repository_root/Apps/Shigure.MacUI/Shigure.MacUI.csproj"
upstream_metadata_path="$repository_root/upstream.json"
sparkle_metadata_path="$script_directory/Sparkle.json"

release_tag="${1:-}"
arm64_app="${2:-}"
arm64_notary_log="${3:-}"
x64_app="${4:-}"
x64_notary_log="${5:-}"
release_notes="${6:-}"
output_directory="${7:-}"
release_repository="${SHIGURE_RELEASE_REPOSITORY:-${GITHUB_REPOSITORY:-}}"

if [[ -z "$release_tag" || -z "$arm64_app" || -z "$arm64_notary_log" \
    || -z "$x64_app" || -z "$x64_notary_log" || -z "$release_notes" \
    || -z "$output_directory" ]]; then
    echo "用法: SHIGURE_RELEASE_REPOSITORY=owner/repo $0 <tag> <arm64.app> <arm64-notary-log.json> <x64.app> <x64-notary-log.json> <release-notes.md> <输出目录>" >&2
    exit 2
fi

if [[ ! "$release_repository" =~ ^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$ ]]; then
    echo "SHIGURE_RELEASE_REPOSITORY 必须是 owner/repo。" >&2
    exit 2
fi

if [[ "$output_directory" == "/" || "$output_directory" == "." || "$output_directory" == ".." ]]; then
    echo "输出目录范围无效: $output_directory" >&2
    exit 2
fi

for app_path in "$arm64_app" "$x64_app"; do
    if [[ ! -d "$app_path" || "$app_path" != *.app ]]; then
        echo "发行输入必须是存在的 .app: $app_path" >&2
        exit 3
    fi
done

for input_file in "$arm64_notary_log" "$x64_notary_log" "$release_notes"; do
    if [[ ! -f "$input_file" ]]; then
        echo "发行输入文件不存在: $input_file" >&2
        exit 3
    fi
done

if [[ "$release_notes" != *.md ]]; then
    echo "发布说明必须是 Markdown 文件: $release_notes" >&2
    exit 3
fi

if [[ -e "$output_directory" ]]; then
    echo "输出已存在，不会覆盖: $output_directory" >&2
    exit 3
fi

output_parent="$(dirname "$output_directory")"
if [[ ! -d "$output_parent" ]]; then
    echo "输出父目录不存在: $output_parent" >&2
    exit 3
fi
output_parent="$(cd "$output_parent" && pwd)"
output_directory="$output_parent/$(basename "$output_directory")"

if ! source_commit="$(git -C "$repository_root" rev-parse --verify HEAD 2>/dev/null)" \
    || [[ ! "$source_commit" =~ ^[0-9a-f]{40}$ ]]; then
    echo "Release staging 必须在具有有效 Git HEAD 的仓库中运行；API 快照不能代替发布提交。" >&2
    exit 4
fi

if ! worktree_status="$(git -C "$repository_root" status --porcelain=v1 --untracked-files=all)"; then
    echo "无法读取 Git 工作区状态。" >&2
    exit 4
fi
if [[ -n "$worktree_status" ]]; then
    echo "Release staging 要求 Git 工作区完全干净（包括未跟踪文件）。" >&2
    exit 4
fi

marketing_version="$(dotnet msbuild "$mac_ui_project" -nologo -getProperty:ShigureMarketingVersion)"
application_version="$(dotnet msbuild "$mac_ui_project" -nologo -getProperty:Version)"
bundle_version="$(dotnet msbuild "$mac_ui_project" -nologo -getProperty:ShigureBundleVersion)"
expected_tag="v$marketing_version"
if [[ "$release_tag" != "$expected_tag" ]]; then
    echo "Release tag 必须与营销版本一致: $release_tag != $expected_tag" >&2
    exit 5
fi

if ! tag_commit="$(git -C "$repository_root" rev-list -n 1 "refs/tags/$release_tag" 2>/dev/null)" \
    || [[ "$tag_commit" != "$source_commit" ]]; then
    echo "Release tag 必须存在并精确指向当前 HEAD。" >&2
    exit 5
fi

source_tree="$(git -C "$repository_root" rev-parse 'HEAD^{tree}')"
source_commit_timestamp="$(git -C "$repository_root" show -s --format=%cI HEAD)"
upstream_repository="$(plutil -extract repository raw "$upstream_metadata_path")"
upstream_branch="$(plutil -extract branch raw "$upstream_metadata_path")"
upstream_commit="$(plutil -extract baselineSha raw "$upstream_metadata_path")"
upstream_tree="$(plutil -extract baselineTreeSha raw "$upstream_metadata_path")"
expected_sparkle_version="$(plutil -extract version raw "$sparkle_metadata_path")"

if ! iconv -f UTF-8 -t UTF-8 "$release_notes" >/dev/null 2>&1; then
    echo "发布说明必须是有效 UTF-8。" >&2
    exit 5
fi
first_notes_line="$(awk 'NF { print; exit }' "$release_notes")"
if [[ "$first_notes_line" != "# Shigure $marketing_version" ]]; then
    echo "发布说明首个非空行必须是: # Shigure $marketing_version" >&2
    exit 5
fi
if (( $(awk 'NF { count++ } END { print count + 0 }' "$release_notes") < 2 )); then
    echo "发布说明不能只有标题。" >&2
    exit 5
fi

build_root="$(mktemp -d "$output_parent/.shigure-release.XXXXXX")"
trap 'rm -rf "$build_root"' EXIT
staging_directory="$build_root/$(basename "$output_directory")"
mkdir -p "$staging_directory/evidence"

validate_app() {
    local app_path="$1"
    local runtime_identifier="$2"
    local expected_architecture="$3"
    local metadata_path="$4"
    local info_plist="$app_path/Contents/Info.plist"

    if [[ ! -f "$info_plist" ]]; then
        echo "应用缺少 Info.plist: $app_path" >&2
        exit 6
    fi

    local bundle_identifier
    local short_version
    local app_bundle_version
    local bundle_executable
    bundle_identifier="$(plutil -extract CFBundleIdentifier raw "$info_plist")"
    short_version="$(plutil -extract CFBundleShortVersionString raw "$info_plist")"
    app_bundle_version="$(plutil -extract CFBundleVersion raw "$info_plist")"
    bundle_executable="$(plutil -extract CFBundleExecutable raw "$info_plist")"
    if [[ "$bundle_identifier" != "com.arasaka.shigure.mac" \
        || "$short_version" != "$marketing_version" \
        || "$app_bundle_version" != "$bundle_version" ]]; then
        echo "应用标识或版本与当前事实源不一致: $app_path" >&2
        exit 6
    fi

    local executable_path="$app_path/Contents/MacOS/$bundle_executable"
    local executable_description
    if [[ ! -f "$executable_path" ]]; then
        echo "应用主程序不存在: $executable_path" >&2
        exit 6
    fi
    executable_description="$(file -b "$executable_path")"
    if [[ "$expected_architecture" == "arm64" ]]; then
        if [[ "$executable_description" != "Mach-O 64-bit executable arm64" ]]; then
            echo "arm64 发行主程序架构不正确: $executable_description" >&2
            exit 6
        fi
    elif [[ "$executable_description" != "Mach-O 64-bit executable x86_64" ]]; then
        echo "x64 发行主程序架构不正确: $executable_description" >&2
        exit 6
    fi

    codesign --verify --deep --strict "$app_path"
    local signature_details
    local team_identifier
    signature_details="$(codesign --display --verbose=4 "$app_path" 2>&1)"
    team_identifier="$(sed -n 's/^TeamIdentifier=//p' <<<"$signature_details")"
    if [[ "$signature_details" != *"Authority=Developer ID Application:"* \
        || "$signature_details" != *"runtime"* || -z "$team_identifier" ]]; then
        echo "发行应用必须使用带 Hardened Runtime 的 Developer ID Application 身份: $app_path" >&2
        exit 6
    fi
    local entitlements
    local allow_jit
    entitlements="$(codesign --display --entitlements - "$app_path" 2>/dev/null)"
    if ! allow_jit="$(/usr/libexec/PlistBuddy -c 'Print :com.apple.security.cs.allow-jit' /dev/stdin <<<"$entitlements" 2>/dev/null)" \
        || [[ "$allow_jit" != "true" ]]; then
        echo "发行应用缺少 .NET 运行所需的 allow-jit entitlement: $app_path" >&2
        exit 6
    fi
    xcrun stapler validate -v "$app_path"
    spctl --assess --type execute --verbose=4 "$app_path"

    local framework_path="$app_path/Contents/Frameworks/Sparkle.framework"
    local sparkle_version
    if [[ ! -d "$framework_path" \
        || ! -f "$app_path/Contents/Resources/ThirdPartyNotices/Sparkle-LICENSE" ]]; then
        echo "发行应用缺少 Sparkle framework 或许可证: $app_path" >&2
        exit 6
    fi
    sparkle_version="$(plutil -extract CFBundleShortVersionString raw "$framework_path/Versions/B/Resources/Info.plist")"
    if [[ "$sparkle_version" != "$expected_sparkle_version" ]]; then
        echo "Sparkle framework 版本与锁文件不一致: $app_path" >&2
        exit 6
    fi
    local framework_signature_details
    local framework_team_identifier
    framework_signature_details="$(codesign --display --verbose=4 "$framework_path" 2>&1)"
    framework_team_identifier="$(sed -n 's/^TeamIdentifier=//p' <<<"$framework_signature_details")"
    if [[ "$framework_signature_details" != *"Authority=Developer ID Application:"* \
        || "$framework_signature_details" != *"runtime"* \
        || "$framework_team_identifier" != "$team_identifier" ]]; then
        echo "Sparkle framework 必须使用与应用相同 Team ID 的 Developer ID Application 身份: $app_path" >&2
        exit 6
    fi

    local feed_url
    local public_ed_key
    local decoded_key_path="$build_root/$runtime_identifier-public-key"
    feed_url="$(plutil -extract SUFeedURL raw "$info_plist")"
    public_ed_key="$(plutil -extract SUPublicEDKey raw "$info_plist")"
    if [[ "$feed_url" != https://* \
        || "$(plutil -extract SURequireSignedFeed raw "$info_plist")" != "true" \
        || "$(plutil -extract SUVerifyUpdateBeforeExtraction raw "$info_plist")" != "true" \
        || "$(plutil -extract SUEnableAutomaticChecks raw "$info_plist")" != "false" \
        || "$(plutil -extract SUAutomaticallyUpdate raw "$info_plist")" != "false" \
        || "$(plutil -extract SUAllowsAutomaticUpdates raw "$info_plist")" != "false" ]]; then
        echo "发行应用的 Sparkle feed 或安全开关无效: $app_path" >&2
        exit 6
    fi
    if ! printf '%s' "$public_ed_key" | openssl base64 -d -A >"$decoded_key_path" 2>/dev/null \
        || [[ "$(wc -c <"$decoded_key_path" | tr -d ' ')" != "32" ]]; then
        echo "发行应用的 EdDSA 公钥格式无效: $app_path" >&2
        exit 6
    fi

    plutil -create xml1 "$metadata_path"
    plutil -insert runtimeIdentifier -string "$runtime_identifier" "$metadata_path"
    plutil -insert architecture -string "$expected_architecture" "$metadata_path"
    plutil -insert bundleIdentifier -string "$bundle_identifier" "$metadata_path"
    plutil -insert feedUrl -string "$feed_url" "$metadata_path"
    plutil -insert publicEdKeySha256 -string "$(shasum -a 256 "$decoded_key_path" | awk '{print $1}')" "$metadata_path"
    plutil -insert teamIdentifier -string "$team_identifier" "$metadata_path"
}

validate_notary_log() {
    local log_path="$1"
    local metadata_path="$2"
    local status
    local issues
    local submission_id
    status="$(plutil -extract status raw "$log_path")"
    issues="$(plutil -extract issues json -o - "$log_path")"
    submission_id="$(plutil -extract id raw "$log_path")"
    if [[ "$status" != "Accepted" || "$issues" != "[]" || -z "$submission_id" ]]; then
        echo "公证日志必须为 Accepted、issues 为空且包含 submission id: $log_path" >&2
        exit 6
    fi
    plutil -create xml1 "$metadata_path"
    plutil -insert submissionId -string "$submission_id" "$metadata_path"
    plutil -insert sha256 -string "$(shasum -a 256 "$log_path" | awk '{print $1}')" "$metadata_path"
}

arm64_metadata="$build_root/arm64.plist"
x64_metadata="$build_root/x64.plist"
arm64_notary_metadata="$build_root/arm64-notary.plist"
x64_notary_metadata="$build_root/x64-notary.plist"
validate_app "$arm64_app" "osx-arm64" "arm64" "$arm64_metadata"
validate_app "$x64_app" "osx-x64" "x86_64" "$x64_metadata"
validate_notary_log "$arm64_notary_log" "$arm64_notary_metadata"
validate_notary_log "$x64_notary_log" "$x64_notary_metadata"

arm64_team="$(plutil -extract teamIdentifier raw "$arm64_metadata")"
x64_team="$(plutil -extract teamIdentifier raw "$x64_metadata")"
arm64_public_key_sha256="$(plutil -extract publicEdKeySha256 raw "$arm64_metadata")"
x64_public_key_sha256="$(plutil -extract publicEdKeySha256 raw "$x64_metadata")"
arm64_feed="$(plutil -extract feedUrl raw "$arm64_metadata")"
x64_feed="$(plutil -extract feedUrl raw "$x64_metadata")"
if [[ "$arm64_team" != "$x64_team" ]]; then
    echo "双架构应用必须使用同一 Team ID。" >&2
    exit 7
fi
if [[ "$arm64_public_key_sha256" != "$x64_public_key_sha256" ]]; then
    echo "双架构应用必须使用同一 EdDSA 公钥。" >&2
    exit 7
fi
if [[ "$arm64_feed" == "$x64_feed" ]]; then
    echo "arm64 与 x64 必须使用不同的 HTTPS appcast。" >&2
    exit 7
fi

arm64_archive_name="Shigure-$marketing_version-macos-arm64.zip"
x64_archive_name="Shigure-$marketing_version-macos-x64.zip"
release_notes_name="Shigure-$marketing_version-release-notes.md"
arm64_notary_name="arm64-notary-log.json"
x64_notary_name="x64-notary-log.json"
ditto -c -k --sequesterRsrc --keepParent "$arm64_app" "$staging_directory/$arm64_archive_name"
ditto -c -k --sequesterRsrc --keepParent "$x64_app" "$staging_directory/$x64_archive_name"
cp "$release_notes" "$staging_directory/$release_notes_name"
cp "$arm64_notary_log" "$staging_directory/evidence/$arm64_notary_name"
cp "$x64_notary_log" "$staging_directory/evidence/$x64_notary_name"

arm64_archive_sha256="$(shasum -a 256 "$staging_directory/$arm64_archive_name" | awk '{print $1}')"
x64_archive_sha256="$(shasum -a 256 "$staging_directory/$x64_archive_name" | awk '{print $1}')"
release_notes_sha256="$(shasum -a 256 "$staging_directory/$release_notes_name" | awk '{print $1}')"
arm64_archive_size="$(stat -f '%z' "$staging_directory/$arm64_archive_name")"
x64_archive_size="$(stat -f '%z' "$staging_directory/$x64_archive_name")"

provenance_plist="$build_root/release-provenance.plist"
provenance_path="$staging_directory/release-provenance.json"
plutil -create xml1 "$provenance_plist"
plutil -insert schemaVersion -integer 1 "$provenance_plist"
plutil -insert releaseRepository -string "$release_repository" "$provenance_plist"
plutil -insert releaseTag -string "$release_tag" "$provenance_plist"
plutil -insert marketingVersion -string "$marketing_version" "$provenance_plist"
plutil -insert applicationVersion -string "$application_version" "$provenance_plist"
plutil -insert bundleVersion -string "$bundle_version" "$provenance_plist"
plutil -insert source -json '{}' "$provenance_plist"
plutil -insert source.commit -string "$source_commit" "$provenance_plist"
plutil -insert source.tree -string "$source_tree" "$provenance_plist"
plutil -insert source.commitTimestamp -string "$source_commit_timestamp" "$provenance_plist"
plutil -insert upstream -json '{}' "$provenance_plist"
plutil -insert upstream.repository -string "$upstream_repository" "$provenance_plist"
plutil -insert upstream.branch -string "$upstream_branch" "$provenance_plist"
plutil -insert upstream.baselineCommit -string "$upstream_commit" "$provenance_plist"
plutil -insert upstream.baselineTree -string "$upstream_tree" "$provenance_plist"
plutil -insert signing -json '{}' "$provenance_plist"
plutil -insert signing.teamIdentifier -string "$arm64_team" "$provenance_plist"
plutil -insert sparkle -json '{}' "$provenance_plist"
plutil -insert sparkle.version -string "$expected_sparkle_version" "$provenance_plist"
plutil -insert sparkle.publicEdKeySha256 -string "$arm64_public_key_sha256" "$provenance_plist"
plutil -insert releaseNotes -json '{}' "$provenance_plist"
plutil -insert releaseNotes.fileName -string "$release_notes_name" "$provenance_plist"
plutil -insert releaseNotes.sha256 -string "$release_notes_sha256" "$provenance_plist"
plutil -insert notarization -json '{}' "$provenance_plist"
plutil -insert notarization.arm64 -json '{}' "$provenance_plist"
plutil -insert notarization.arm64.submissionId -string "$(plutil -extract submissionId raw "$arm64_notary_metadata")" "$provenance_plist"
plutil -insert notarization.arm64.logFileName -string "evidence/$arm64_notary_name" "$provenance_plist"
plutil -insert notarization.arm64.logSha256 -string "$(plutil -extract sha256 raw "$arm64_notary_metadata")" "$provenance_plist"
plutil -insert notarization.x64 -json '{}' "$provenance_plist"
plutil -insert notarization.x64.submissionId -string "$(plutil -extract submissionId raw "$x64_notary_metadata")" "$provenance_plist"
plutil -insert notarization.x64.logFileName -string "evidence/$x64_notary_name" "$provenance_plist"
plutil -insert notarization.x64.logSha256 -string "$(plutil -extract sha256 raw "$x64_notary_metadata")" "$provenance_plist"
plutil -insert artifacts -json '[]' "$provenance_plist"
plutil -insert artifacts.0 -json '{}' "$provenance_plist"
plutil -insert artifacts.0.runtimeIdentifier -string osx-arm64 "$provenance_plist"
plutil -insert artifacts.0.architecture -string arm64 "$provenance_plist"
plutil -insert artifacts.0.fileName -string "$arm64_archive_name" "$provenance_plist"
plutil -insert artifacts.0.sha256 -string "$arm64_archive_sha256" "$provenance_plist"
plutil -insert artifacts.0.sizeBytes -integer "$arm64_archive_size" "$provenance_plist"
plutil -insert artifacts.0.feedUrl -string "$arm64_feed" "$provenance_plist"
plutil -insert artifacts.1 -json '{}' "$provenance_plist"
plutil -insert artifacts.1.runtimeIdentifier -string osx-x64 "$provenance_plist"
plutil -insert artifacts.1.architecture -string x86_64 "$provenance_plist"
plutil -insert artifacts.1.fileName -string "$x64_archive_name" "$provenance_plist"
plutil -insert artifacts.1.sha256 -string "$x64_archive_sha256" "$provenance_plist"
plutil -insert artifacts.1.sizeBytes -integer "$x64_archive_size" "$provenance_plist"
plutil -insert artifacts.1.feedUrl -string "$x64_feed" "$provenance_plist"
plutil -convert json -o "$provenance_path" "$provenance_plist"

(
    cd "$staging_directory"
    find . -type f ! -name SHA256SUMS -print0 \
        | LC_ALL=C sort -z \
        | xargs -0 shasum -a 256 \
        | sed 's#  \./#  #' >SHA256SUMS
    shasum -a 256 -c SHA256SUMS
)

if [[ -e "$output_directory" ]]; then
    echo "输出在组装期间出现，不会覆盖: $output_directory" >&2
    exit 8
fi
mv -n "$staging_directory" "$output_directory"
if [[ -d "$staging_directory" ]]; then
    echo "无法原子发布 staging 目录: $output_directory" >&2
    exit 8
fi

echo "Release staging 已生成: $output_directory"
echo "此操作未创建或修改任何外部 Release。"
