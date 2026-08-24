#!/bin/bash
set -euo pipefail

script_directory="$(cd "$(dirname "$0")" && pwd)"
repository_root="$(cd "$script_directory/../.." && pwd)"
mac_ui_project="$repository_root/Apps/Shigure.MacUI/Shigure.MacUI.csproj"
output_path="${1:-$repository_root/artifacts/macos/Shigure.app}"
codesign_identity="${SHIGURE_CODESIGN_IDENTITY:-}"
runtime_identifier="${SHIGURE_RUNTIME_IDENTIFIER:-}"
entitlements_path="$script_directory/Shigure.entitlements"
local_signing_identity_script="$script_directory/ensure-local-signing-identity.sh"
sparkle_metadata_path="$script_directory/Sparkle.json"
sparkle_archive="${SHIGURE_SPARKLE_ARCHIVE:-}"
sparkle_feed_url="${SHIGURE_SPARKLE_FEED_URL:-}"
sparkle_public_ed_key="${SHIGURE_SPARKLE_PUBLIC_ED_KEY:-}"
codesign_timestamp_arguments=(--timestamp)
using_local_signing_identity=false

if [[ -z "$codesign_identity" ]]; then
    if ! codesign_identity="$("$local_signing_identity_script" --find)"; then
        echo "缺少持久签名身份。请先运行 Packaging/macOS/ensure-local-signing-identity.sh；如确需临时预览包，请显式设置 SHIGURE_CODESIGN_IDENTITY=-。" >&2
        exit 7
    fi

    codesign_timestamp_arguments=(--timestamp=none)
    using_local_signing_identity=true
    echo "正在复用本机持久签名身份。" >&2
fi

sign_executable_code() {
    local include_entitlements="$1"
    local code_path="$2"

    if [[ "$using_local_signing_identity" == true ]]; then
        codesign --force --sign "$codesign_identity" "${codesign_timestamp_arguments[@]}" "$code_path"
    elif [[ "$include_entitlements" == true ]]; then
        codesign --force --sign "$codesign_identity" "${codesign_timestamp_arguments[@]}" \
            --options runtime \
            --entitlements "$entitlements_path" \
            "$code_path"
    else
        codesign --force --sign "$codesign_identity" "${codesign_timestamp_arguments[@]}" \
            --options runtime \
            "$code_path"
    fi
}

if [[ "$output_path" != *.app ]]; then
    echo "输出路径必须以 .app 结尾: $output_path" >&2
    exit 2
fi

if [[ -e "$output_path" ]]; then
    echo "输出已存在，不会覆盖: $output_path" >&2
    exit 3
fi

if [[ -z "$runtime_identifier" ]]; then
    case "$(uname -m)" in
        arm64) runtime_identifier="osx-arm64" ;;
        x86_64) runtime_identifier="osx-x64" ;;
        *)
            echo "不支持的主机架构；请设置 SHIGURE_RUNTIME_IDENTIFIER=osx-arm64|osx-x64。" >&2
            exit 4
            ;;
    esac
fi

if [[ "$runtime_identifier" != "osx-arm64" && "$runtime_identifier" != "osx-x64" ]]; then
    echo "SHIGURE_RUNTIME_IDENTIFIER 必须是 osx-arm64 或 osx-x64: $runtime_identifier" >&2
    exit 4
fi

marketing_version="$(dotnet msbuild "$mac_ui_project" -nologo -getProperty:ShigureMarketingVersion)"
bundle_version="$(dotnet msbuild "$mac_ui_project" -nologo -getProperty:ShigureBundleVersion)"
application_version="$(dotnet msbuild "$mac_ui_project" -nologo -getProperty:Version)"

if [[ ! "$marketing_version" =~ ^([1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$ ]]; then
    echo "ShigureMarketingVersion 必须是三个非负整数，且 major 大于 0: $marketing_version" >&2
    exit 5
fi

if [[ ! "$bundle_version" =~ ^([1-9][0-9]{0,3})\.(0|[1-9][0-9]?)\.(0|[1-9][0-9]?)$ ]]; then
    echo "ShigureBundleVersion 必须符合 Apple major.minor.build 长度约束: $bundle_version" >&2
    exit 5
fi

if [[ "$application_version" != "$marketing_version."* ]]; then
    echo "Version 必须从 ShigureMarketingVersion 派生: $application_version" >&2
    exit 5
fi

output_parent="$(dirname "$output_path")"
mkdir -p "$output_parent"
build_root="$(mktemp -d "$output_parent/.shigure-build.XXXXXX")"
trap 'rm -rf "$build_root"' EXIT

app_path="$build_root/Shigure.app"
contents_path="$app_path/Contents"
macos_path="$contents_path/MacOS"
resources_path="$contents_path/Resources"
frameworks_path="$contents_path/Frameworks"
runtime_baseline_path="$resources_path/runtime-baseline"
mkdir -p "$macos_path" "$resources_path"

dotnet publish "$mac_ui_project" \
    --configuration Release \
    --runtime "$runtime_identifier" \
    --self-contained true \
    -p:DebugType=None \
    -p:DebugSymbols=false \
    --output "$macos_path"

xcrun swiftc \
    -parse-as-library \
    -emit-library \
    -O \
    -framework ScreenCaptureKit \
    -framework CoreMedia \
    -framework CoreVideo \
    -framework CoreGraphics \
    "$script_directory/ShigureCapture.swift" \
    -o "$macos_path/libShigureCapture.dylib"

mkdir -p "$runtime_baseline_path"
mv "$macos_path/Fuyutsui" "$runtime_baseline_path/Fuyutsui"
mv "$macos_path/config" "$runtime_baseline_path/config"
mv "$macos_path/keymap" "$runtime_baseline_path/keymap"
mv "$macos_path/wow_process.txt" "$runtime_baseline_path/wow_process.txt"

iconset_path="$build_root/Shigure.iconset"
mkdir -p "$iconset_path"
icon_source="$repository_root/Assets/arasaka-icon-transparent.png"
sips -z 16 16 "$icon_source" --out "$iconset_path/icon_16x16.png" >/dev/null
sips -z 32 32 "$icon_source" --out "$iconset_path/icon_16x16@2x.png" >/dev/null
sips -z 32 32 "$icon_source" --out "$iconset_path/icon_32x32.png" >/dev/null
sips -z 64 64 "$icon_source" --out "$iconset_path/icon_32x32@2x.png" >/dev/null
sips -z 128 128 "$icon_source" --out "$iconset_path/icon_128x128.png" >/dev/null
sips -z 256 256 "$icon_source" --out "$iconset_path/icon_128x128@2x.png" >/dev/null
sips -z 256 256 "$icon_source" --out "$iconset_path/icon_256x256.png" >/dev/null
sips -z 512 512 "$icon_source" --out "$iconset_path/icon_256x256@2x.png" >/dev/null
sips -z 512 512 "$icon_source" --out "$iconset_path/icon_512x512.png" >/dev/null
sips -z 1024 1024 "$icon_source" --out "$iconset_path/icon_512x512@2x.png" >/dev/null
iconutil -c icns "$iconset_path" -o "$resources_path/Shigure.icns"

cp "$script_directory/Info.plist" "$contents_path/Info.plist"
plutil -replace CFBundleShortVersionString -string "$marketing_version" "$contents_path/Info.plist"
plutil -replace CFBundleVersion -string "$bundle_version" "$contents_path/Info.plist"

if [[ -n "$sparkle_archive" || -n "$sparkle_feed_url" || -n "$sparkle_public_ed_key" ]]; then
    if [[ -z "$sparkle_archive" || ! -f "$sparkle_archive" ]]; then
        echo "SHIGURE_SPARKLE_ARCHIVE 必须指向锁定的 Sparkle 发行归档。" >&2
        exit 6
    fi
    if [[ "$sparkle_feed_url" != https://* ]]; then
        echo "SHIGURE_SPARKLE_FEED_URL 必须是当前 RID 的 HTTPS appcast URL。" >&2
        exit 6
    fi
    if [[ -z "$sparkle_public_ed_key" ]]; then
        echo "SHIGURE_SPARKLE_PUBLIC_ED_KEY 不能为空。" >&2
        exit 6
    fi

    decoded_key_path="$build_root/sparkle-public-key"
    if ! printf '%s' "$sparkle_public_ed_key" | openssl base64 -d -A >"$decoded_key_path" 2>/dev/null \
        || [[ "$(wc -c <"$decoded_key_path" | tr -d ' ')" != "32" ]]; then
        echo "SHIGURE_SPARKLE_PUBLIC_ED_KEY 必须是 32 字节 Ed25519 公钥的 Base64 编码。" >&2
        exit 6
    fi

    expected_sparkle_sha256="$(plutil -extract archiveSha256 raw "$sparkle_metadata_path")"
    actual_sparkle_sha256="$(shasum -a 256 "$sparkle_archive" | awk '{print $1}')"
    if [[ "$actual_sparkle_sha256" != "$expected_sparkle_sha256" ]]; then
        echo "Sparkle 发行归档 SHA-256 不匹配。" >&2
        exit 6
    fi

    sparkle_extract_path="$build_root/sparkle-distribution"
    mkdir -p "$sparkle_extract_path" "$frameworks_path"
    tar -xf "$sparkle_archive" -C "$sparkle_extract_path" ./Sparkle.framework ./LICENSE
    expected_sparkle_version="$(plutil -extract version raw "$sparkle_metadata_path")"
    actual_sparkle_version="$(plutil -extract CFBundleShortVersionString raw "$sparkle_extract_path/Sparkle.framework/Versions/B/Resources/Info.plist")"
    if [[ "$actual_sparkle_version" != "$expected_sparkle_version" ]]; then
        echo "Sparkle framework 版本与锁文件不一致。" >&2
        exit 6
    fi
    ditto "$sparkle_extract_path/Sparkle.framework" "$frameworks_path/Sparkle.framework"
    mkdir -p "$resources_path/ThirdPartyNotices"
    cp "$sparkle_extract_path/LICENSE" "$resources_path/ThirdPartyNotices/Sparkle-LICENSE"

    plutil -insert SUFeedURL -string "$sparkle_feed_url" "$contents_path/Info.plist"
    plutil -insert SUPublicEDKey -string "$sparkle_public_ed_key" "$contents_path/Info.plist"
    plutil -insert SURequireSignedFeed -bool true "$contents_path/Info.plist"
    plutil -insert SUVerifyUpdateBeforeExtraction -bool true "$contents_path/Info.plist"
    plutil -insert SUEnableAutomaticChecks -bool false "$contents_path/Info.plist"
    plutil -insert SUAutomaticallyUpdate -bool false "$contents_path/Info.plist"
    plutil -insert SUAllowsAutomaticUpdates -bool false "$contents_path/Info.plist"
else
    echo "提示: 当前构建未配置 Sparkle，应用内更新入口将显示不可用。" >&2
fi

chmod +x "$macos_path/Shigure.MacUI"
xattr -cr "$app_path"
if [[ "$codesign_identity" == "-" ]]; then
    echo "警告: 正在生成 ad-hoc 开发预览包；重新构建会改变 TCC 代码需求并使已有权限失效。" >&2
    codesign --force --deep --sign - --timestamp=none "$app_path"
else
    if [[ -d "$frameworks_path/Sparkle.framework" ]]; then
        while IFS= read -r -d '' nested_code; do
            file_description="$(file -b "$nested_code")"
            if [[ "$file_description" != *"Mach-O"* ]]; then
                continue
            fi

            if [[ "$file_description" == *"executable"* ]]; then
                sign_executable_code false "$nested_code"
            else
                codesign --force --sign "$codesign_identity" "${codesign_timestamp_arguments[@]}" "$nested_code"
            fi
        done < <(find "$frameworks_path/Sparkle.framework" -type f -print0)

        while IFS= read -r -d '' xpc_bundle; do
            sign_executable_code false "$xpc_bundle"
        done < <(find "$frameworks_path/Sparkle.framework" -type d -name '*.xpc' -print0)

        while IFS= read -r -d '' helper_app; do
            sign_executable_code false "$helper_app"
        done < <(find "$frameworks_path/Sparkle.framework" -type d -name '*.app' -print0)

        codesign --force --sign "$codesign_identity" "${codesign_timestamp_arguments[@]}" "$frameworks_path/Sparkle.framework"
    fi

    while IFS= read -r -d '' nested_code; do
        file_description="$(file -b "$nested_code")"
        if [[ "$file_description" == *"Mach-O"* && "$file_description" == *"executable"* ]]; then
            sign_executable_code true "$nested_code"
        else
            codesign --force --sign "$codesign_identity" "${codesign_timestamp_arguments[@]}" "$nested_code"
        fi
    done < <(find "$macos_path" -type f ! -path "$macos_path/Shigure.MacUI" -print0)

    sign_executable_code true "$app_path"

    signature_details="$(codesign --display --verbose=4 "$app_path" 2>&1)"
    if [[ "$using_local_signing_identity" == false ]]; then
        if [[ "$signature_details" != *"runtime"* ]]; then
            echo "Apple 持久签名未启用 Hardened Runtime。" >&2
            exit 5
        fi

        signed_entitlements="$(codesign --display --entitlements - "$app_path" 2>/dev/null)"
        if [[ "$signed_entitlements" != *"com.apple.security.cs.allow-jit"* ]]; then
            echo "Apple 持久签名缺少 .NET 运行所需的 allow-jit entitlement。" >&2
            exit 5
        fi
    fi

    designated_requirement="$(codesign --display --requirements - "$app_path" 2>&1)"
    if [[ "$designated_requirement" == *"cdhash"* ]]; then
        echo "持久签名仍绑定当前代码哈希，无法保持 TCC 授权。" >&2
        exit 5
    fi
fi

plutil -lint "$contents_path/Info.plist"
if [[ "$(plutil -extract CFBundleShortVersionString raw "$contents_path/Info.plist")" != "$marketing_version" ]]; then
    echo "生成的 CFBundleShortVersionString 与版本事实源不一致。" >&2
    exit 5
fi
if [[ "$(plutil -extract CFBundleVersion raw "$contents_path/Info.plist")" != "$bundle_version" ]]; then
    echo "生成的 CFBundleVersion 与版本事实源不一致。" >&2
    exit 5
fi
file "$macos_path/Shigure.MacUI"
codesign --verify --deep --strict "$app_path"

mv "$app_path" "$output_path"
echo "已生成 v$application_version ($runtime_identifier): $output_path"
