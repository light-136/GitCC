#!/usr/bin/env bash
# =============================================================================
# DataScope Studio 冒烟测试（Smoke，V2-执行③：测试分层 Smoke 层）
#
# 目的：快速验证"核心可执行文件能否正常启动"，作为发布前的最低门槛。
#   覆盖：主程序 DataScope + 模拟设备 simulator 的全部运行模式。
#   原理：后台启动可执行文件 → 等待 1.5 秒 → 若进程仍存活且未退出
#         说明事件循环正常运行（启动即崩溃/参数错误会立即退出并给非零码）。
#
# 用法：bash tests/smoke/smoke.sh [构建目录]    默认 build
# 退出码：0 = 全部通过；非 0 = 有程序启动失败
# =============================================================================
set -u

BUILD_DIR="${1:-build}"
FAIL=0

# 统一检查：后台启动 → 探活 → 收尾
run_alive() {
    local exe="$1"; shift
    "$exe" "$@" &
    local pid=$!
    sleep 1.5
    if kill -0 "$pid" 2>/dev/null; then
        echo "PASS: $exe $*"
        kill "$pid" 2>/dev/null
        wait "$pid" 2>/dev/null
    else
        wait "$pid"
        echo "FAIL: $exe $* exited=$?"
        FAIL=1
    fi
}

echo "== 冒烟测试开始（构建目录: $BUILD_DIR）=="

# ---- 主程序（GUI）----
run_alive "$BUILD_DIR/DataScope.exe"

# ---- 模拟设备：全部运行模式 + 关键异常注入 ----
run_alive "$BUILD_DIR/simulator.exe" -p 49871 --mode normal
run_alive "$BUILD_DIR/simulator.exe" -p 49872 --mode alarm
run_alive "$BUILD_DIR/simulator.exe" -p 49873 --mode stress
run_alive "$BUILD_DIR/simulator.exe" -p 49874 --mode busy
run_alive "$BUILD_DIR/simulator.exe" -p 49875 --mode error --fault crc
run_alive "$BUILD_DIR/simulator.exe" -p 49876 --mode error --fault sticky
run_alive "$BUILD_DIR/simulator.exe" -p 49877 --mode error --fault fragment
run_alive "$BUILD_DIR/simulator.exe" -p 49878 --mode error --fault illegal
run_alive "$BUILD_DIR/simulator.exe" -p 49879 --mode error --fault delay
run_alive "$BUILD_DIR/simulator.exe" -p 49880 --mode error --fault surge
run_alive "$BUILD_DIR/simulator.exe" -p 49881 --mode error --fault disconnect

echo "== 冒烟测试结束 =="
if [ "$FAIL" -eq 0 ]; then
    echo "SMOKE ALL PASS"
    exit 0
else
    echo "SMOKE FAILED"
    exit 1
fi
