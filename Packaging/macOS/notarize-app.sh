#!/bin/bash
set -euo pipefail

app_path="${1:-}"
notary_profile="${SHIGURE_NOTARYTOOL_PROFILE:-}"
notary_timeout="${SHIGURE_NOTARY_TIMEOUT:-30m}"

if [[ -z "$app_path" || "$app_path" != *.app ]]; then
    echo "用法: SHIGURE_NOTARYTOOL_PROFILE=<钥匙链配置名> $0 <已签名的 .app>" >&2
    exit 2
fi

if [[ ! -d "$app_path" ]]; then
    echo "应用不存在: $app_path" >&2
    exit 3
fi

app_directory="$(cd "$(dirname "$app_path")" && pwd)"
app_path="$app_directory/$(basename "$app_path")"

if [[ -z "$notary_profile" ]]; then
    echo "必须设置 SHIGURE_NOTARYTOOL_PROFILE；请先用 notarytool store-credentials 将凭据存入钥匙链。" >&2
    exit 4
fi

notary_log_path="${SHIGURE_NOTARY_LOG_PATH:-$app_path.notary-log.json}"
if [[ -e "$notary_log_path" ]]; then
    echo "公证日志已存在，不会覆盖: $notary_log_path" >&2
    exit 5
fi

notary_log_directory="$(dirname "$notary_log_path")"
if [[ ! -d "$notary_log_directory" ]]; then
    echo "公证日志目录不存在: $notary_log_directory" >&2
    exit 5
fi

notary_log_directory="$(cd "$notary_log_directory" && pwd)"
notary_log_path="$notary_log_directory/$(basename "$notary_log_path")"
if [[ "$notary_log_path" == "$app_path/"* ]]; then
    echo "公证日志不能写入已签名应用内部: $notary_log_path" >&2
    exit 5
fi

codesign --verify --deep --strict "$app_path"
signature_details="$(codesign --display --verbose=4 "$app_path" 2>&1)"
if [[ "$signature_details" != *"Authority=Developer ID Application:"* ]]; then
    echo "应用必须使用 Developer ID Application 身份签名。" >&2
    exit 6
fi

if [[ "$signature_details" != *"runtime"* ]]; then
    echo "应用签名未启用 Hardened Runtime。" >&2
    exit 6
fi

entitlements="$(codesign --display --entitlements - "$app_path" 2>/dev/null)"
if [[ "$entitlements" != *"com.apple.security.cs.allow-jit"* ]]; then
    echo "应用签名缺少 .NET 运行所需的 allow-jit entitlement。" >&2
    exit 6
fi

temporary_root="$(mktemp -d "${TMPDIR:-/tmp}/shigure-notary.XXXXXX")"
trap 'rm -rf "$temporary_root"' EXIT
archive_path="$temporary_root/Shigure.zip"
submission_result="$temporary_root/submission.json"

ditto -c -k --keepParent "$app_path" "$archive_path"
if ! xcrun notarytool submit "$archive_path" \
    --keychain-profile "$notary_profile" \
    --wait \
    --timeout "$notary_timeout" \
    --output-format json >"$submission_result"; then
    echo "公证提交失败；应用未执行 staple。" >&2
    exit 7
fi

submission_id="$(plutil -extract id raw "$submission_result")"
submission_status="$(plutil -extract status raw "$submission_result")"
xcrun notarytool log --keychain-profile "$notary_profile" \
    "$submission_id" "$notary_log_path"

if [[ "$submission_status" != "Accepted" ]]; then
    echo "公证未通过（$submission_status）；详情: $notary_log_path" >&2
    exit 8
fi

notary_issues="$(plutil -extract issues json -o - "$notary_log_path")"
if [[ "$notary_issues" != "[]" ]]; then
    echo "公证日志包含问题；应用未执行 staple。详情: $notary_log_path" >&2
    exit 9
fi

xcrun stapler staple -v "$app_path"
xcrun stapler validate -v "$app_path"
codesign --verify --deep --strict "$app_path"
spctl --assess --type execute --verbose=4 "$app_path"

echo "公证、staple 与 Gatekeeper 校验通过: $app_path"
echo "公证日志: $notary_log_path"
