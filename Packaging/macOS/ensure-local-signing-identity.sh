#!/bin/bash
set -euo pipefail

identity_name="Shigure Local Code Signing"
mode="${1:-ensure}"

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
        | awk -v name="$identity_name" 'index($0, "\"" name "\"") { print $2; exit }'
}

identity_hash="$(find_identity)"
if [[ -n "$identity_hash" ]]; then
    printf '%s\n' "$identity_hash"
    exit 0
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

echo "已创建本机专用签名身份：$identity_name" >&2
printf '%s\n' "$identity_hash"
