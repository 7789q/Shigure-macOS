#!/bin/bash
set -euo pipefail

identity_name="Shigure Local Code Signing"
mode="${1:-ensure}"
signing_state_directory="$HOME/Library/Application Support/Shigure"
identity_pin_path="$signing_state_directory/local-signing-identity.sha1"

if [[ $# -gt 1 || ("$mode" != "ensure" && "$mode" != "--find") ]]; then
    echo "用法: $0 [--find]" >&2
    exit 2
fi

keychain_path="$(security default-keychain -d user | tr -d '"[:space:]')"
if [[ -z "$keychain_path" || ! -f "$keychain_path" ]]; then
    echo "无法定位当前用户的默认钥匙串。" >&2
    exit 3
fi

find_identity() {
    security find-identity -v -p codesigning "$keychain_path" 2>/dev/null \
        | awk -v name="$identity_name" '
            index($0, "\"" name "\"") {
                hashes[++count] = toupper($2)
            }
            END {
                if (count > 1) {
                    print "钥匙串中存在多个同名 Shigure 签名身份，无法确定统一签名证书。" > "/dev/stderr"
                    exit 7
                }
                if (count == 1) {
                    print hashes[1]
                }
            }'
}

identity_hash="$(find_identity)"
if [[ -n "$identity_hash" ]]; then
    if [[ -f "$identity_pin_path" ]]; then
        pinned_identity_hash="$(tr -d '[:space:]' <"$identity_pin_path" | tr '[:lower:]' '[:upper:]')"
        if [[ ! "$pinned_identity_hash" =~ ^[0-9A-F]{40}$ ]]; then
            echo "本地签名身份固定记录无效，请先检查: $identity_pin_path" >&2
            exit 8
        fi
        if [[ "$identity_hash" != "$pinned_identity_hash" ]]; then
            echo "当前 Shigure 签名身份与已固定证书不一致，已拒绝打包以避免系统权限失效。" >&2
            echo "固定指纹: $pinned_identity_hash" >&2
            echo "当前指纹: $identity_hash" >&2
            exit 8
        fi
    else
        umask 077
        mkdir -p "$signing_state_directory"
        printf '%s\n' "$identity_hash" >"$identity_pin_path"
        echo "已固定本机 Shigure 签名身份，后续打包不会静默更换证书。" >&2
    fi

    printf '%s\n' "$identity_hash"
    exit 0
fi

if [[ -f "$identity_pin_path" ]]; then
    echo "已固定的 Shigure 签名身份不在当前钥匙串中，已拒绝创建新证书以避免系统权限失效。" >&2
    echo "请恢复对应钥匙串身份；固定记录位于: $identity_pin_path" >&2
    exit 8
fi

if [[ "$mode" == "--find" ]]; then
    exit 1
fi

if security find-certificate -c "$identity_name" "$keychain_path" >/dev/null 2>&1; then
    echo "钥匙串中存在同名证书，但没有可用私钥；请先修复或移除该证书。" >&2
    exit 4
fi

for command_name in openssl security; do
    if ! command -v "$command_name" >/dev/null 2>&1; then
        echo "缺少创建本地签名身份所需的命令: $command_name" >&2
        exit 5
    fi
done

temporary_directory="$(mktemp -d /private/tmp/shigure-local-signing.XXXXXX)"
cleanup() {
    if [[ "$temporary_directory" == /private/tmp/shigure-local-signing.* ]]; then
        rm -rf "$temporary_directory"
    fi
}
trap cleanup EXIT

private_key_path="$temporary_directory/private-key.pem"
certificate_path="$temporary_directory/certificate.pem"
identity_path="$temporary_directory/identity.p12"
identity_password="$(openssl rand -hex 32)"

openssl req -x509 -newkey rsa:3072 -sha256 -days 7305 -nodes \
    -subj "/CN=$identity_name/O=Shigure Local Development" \
    -addext "keyUsage=critical,digitalSignature" \
    -addext "extendedKeyUsage=codeSigning" \
    -keyout "$private_key_path" \
    -out "$certificate_path" >/dev/null 2>&1

openssl pkcs12 -export -legacy \
    -inkey "$private_key_path" \
    -in "$certificate_path" \
    -out "$identity_path" \
    -passout "pass:$identity_password" >/dev/null 2>&1

security import "$identity_path" \
    -k "$keychain_path" \
    -P "$identity_password" \
    -x \
    -T /usr/bin/codesign \
    -T /usr/bin/security >/dev/null
security add-trusted-cert -r trustRoot -p codeSign -k "$keychain_path" "$certificate_path"

identity_hash="$(find_identity)"
if [[ -z "$identity_hash" ]]; then
    echo "本地签名身份已导入，但 codesign 无法使用它。" >&2
    exit 6
fi

umask 077
mkdir -p "$signing_state_directory"
printf '%s\n' "$identity_hash" >"$identity_pin_path"
echo "已创建本机专用签名身份：$identity_name" >&2
echo "已固定签名身份指纹，后续打包不会静默更换证书。" >&2
printf '%s\n' "$identity_hash"
