#!/bin/bash
set -euo pipefail

script_directory="$(cd "$(dirname "$0")" && pwd)"
repository_root="$(cd "$script_directory/../.." && pwd)"
build_script="$script_directory/build-app.sh"
output_path=""
mode="auto"
validation_status=0
validation_label="未执行完整验证"

usage() {
    cat >&2 <<'EOF'
用法: repackage-local.sh [--fast|--full] [输出.app路径]

默认模式会根据当前工作区是否有源码、配置、Lua 或打包脚本改动自动选择验证级别。
  --fast  只打包并做打包后的签名和启动验收
  --full  强制执行完整验证后再打包
EOF
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --fast)
            mode="fast"
            shift
            ;;
        --full)
            mode="full"
            shift
            ;;
        --help|-h)
            usage
            exit 0
            ;;
        --*)
            echo "未知选项: $1" >&2
            usage
            exit 2
            ;;
        *)
            if [[ -n "$output_path" ]]; then
                echo "只能指定一个输出路径: $1" >&2
                usage
                exit 2
            fi
            output_path="$1"
            shift
            ;;
    esac
done

if [[ -z "$output_path" ]]; then
    output_path="$repository_root/artifacts/macos/Shigure-$(date +%Y%m%d)-local-1.app"
    suffix=2
    while [[ -e "$output_path" ]]; do
        output_path="$repository_root/artifacts/macos/Shigure-$(date +%Y%m%d)-local-$suffix.app"
        suffix=$((suffix + 1))
    done
elif [[ "$output_path" != /* ]]; then
    output_path="$repository_root/$output_path"
fi

if [[ "$output_path" != *.app ]]; then
    echo "输出路径必须以 .app 结尾: $output_path" >&2
    exit 2
fi
if [[ -e "$output_path" ]]; then
    echo "输出已存在，不会覆盖: $output_path" >&2
    exit 2
fi

for command_name in dotnet xcrun codesign open security pgrep; do
    if ! command -v "$command_name" >/dev/null 2>&1; then
        echo "缺少必要命令: $command_name" >&2
        exit 3
    fi
done

if ! security default-keychain -d user >/dev/null 2>&1; then
    echo "无法访问当前用户登录钥匙串。请从 macOS 系统 Terminal 执行此脚本，不要从受限沙箱终端执行。" >&2
    exit 4
fi

if [[ "$mode" == "auto" ]]; then
    mode="fast"
    while IFS= read -r changed_file; do
        case "$changed_file" in
            Apps/*|Core/*|Infrastructure/*|Modules/*|Presentation/*|Runtime/*|config/*|keymap/*|Fuyutsui/*|Packaging/macOS/*.sh|Tests/*)
                mode="full"
                break
                ;;
        esac
    done < <(git -C "$repository_root" status --short --untracked-files=all | sed -E 's/^...//')
fi

run_full_validation() {
    local lua_file
    local test_status

    echo "[1/4] dotnet restore"
    run_with_timeout 60 dotnet restore "$repository_root/Shigure.slnx" || return 1

    echo "[2/4] dotnet build (Release)"
    run_with_timeout 180 dotnet build "$repository_root/Shigure.slnx" --configuration Release --no-restore || return 1

    echo "[3/4] Core contract tests"
    set +e
    dotnet run --project "$repository_root/Tests/Shigure.Core.ContractTests/Shigure.Core.ContractTests.csproj" \
        --configuration Release --no-build
    test_status=$?
    set -e
    if [[ "$test_status" -ne 0 ]]; then
        echo "contract tests 失败（退出码 ${test_status}），继续生成本地诊断包。" >&2
        validation_status=10
        validation_label="完整验证失败，已生成诊断包"
    fi

    echo "[4/5] macOS shell 语法检查"
    bash -n "$script_directory"/*.sh || return 1

    echo "[5/5] Lua 语法检查"
    while IFS= read -r lua_file; do
        [[ -z "$lua_file" ]] && continue
        luajit -b "$repository_root/$lua_file" /dev/null || return 1
    done < <(git -C "$repository_root" status --short --untracked-files=all | awk '$2 ~ /\.lua$/ {print $2}')

    if [[ "$validation_status" -eq 0 ]]; then
        validation_label="完整验证通过"
    fi
    return 0
}

run_with_timeout() {
    local timeout_seconds="$1"
    shift
    "$@" &
    local child_pid=$!
    local elapsed=0

    while kill -0 "$child_pid" >/dev/null 2>&1; do
        if (( elapsed >= timeout_seconds )); then
            kill "$child_pid" >/dev/null 2>&1 || true
            wait "$child_pid" >/dev/null 2>&1 || true
            echo "命令超过 ${timeout_seconds}s 未完成，已停止: $*" >&2
            return 124
        fi
        sleep 1
        elapsed=$((elapsed + 1))
    done

    wait "$child_pid"
}

echo "[预检] 环境和输出路径"
echo "[预检] 模式: $mode"
echo "[预检] 输出: $output_path"

if [[ "$mode" == "full" ]]; then
    if ! command -v luajit >/dev/null 2>&1; then
        echo "缺少完整验证所需命令: luajit" >&2
        exit 3
    fi
    set -e
    run_full_validation
else
    validation_label="快速打包，未执行完整验证"
fi

echo "[打包] build-app.sh"
run_with_timeout 300 "$build_script" "$output_path"

echo "[验收] codesign --verify"
codesign --verify --deep --strict "$output_path"

echo "[验收] Launch Services 启动"
open -n "$output_path"
sleep 3
if ! pgrep -f "$output_path/Contents/MacOS/Shigure.MacUI" >/dev/null 2>&1; then
    echo "Shigure.MacUI 未保持运行: $output_path" >&2
    exit 11
fi

echo "完成: $output_path"
echo "验证状态: $validation_label"
exit "$validation_status"
