# =============================================================================
# DataScope 发布包 E2E 探针（tests/e2e/probe_simulator.ps1）
#
# 目的：对【独立发布包】里的 simulator 做真实 TCP 协议验证，证明发布包设备侧
#   完整可用（非"能启动"而已）：
#     ① 启动 deploy/simulator（normal 模式，指定端口）
#     ② TCP 连接 → 先清空采集帧（让设备端 socket 就绪）
#     ③ 发送通道配置查询帧 {0x03, 0x01}（按契约 03/05）
#     ④ 轮询读取 → 校验应答：SOF=AA55 / FUNC=0x83 / CMD=0x01 / count≥1 / 帧长合法
#     ⑤ 持续读取 → 校验收到 0x01 采集上报帧（normal 正弦，payload=4×float 大端）
#
# 用法：powershell -ExecutionPolicy Bypass -File tests/e2e/probe_simulator.ps1 ^
#          [-Exe deploy/simulator.exe] [-Port 41001]
# 退出码：0 = PASS；非 0 = FAIL
# 注意：本文件必须为 UTF-8 with BOM（PowerShell 5.1 按 ANSI 解析无 BOM 的 UTF-8
#       会因中文字节撞引号而解析失败，详见 docs/05 常见问题）。
# =============================================================================
param(
    [string]$Exe = "deploy/simulator.exe",
    [int]$Port = 41001
)

$ErrorActionPreference = "Stop"

# ---- MODBUS CRC16（协议契约：覆盖 FUNC..DATA，发送低字节在前）----
function Get-ModbusCrc([byte[]]$data) {
    $crc = 0xFFFF
    foreach ($b in $data) {
        $crc = ($crc -bxor $b) -band 0xFFFF
        for ($i = 0; $i -lt 8; $i++) {
            if (($crc -band 1) -ne 0) { $crc = (($crc -shr 1) -bxor 0xA001) -band 0xFFFF }
            else                       { $crc = ($crc -shr 1) -band 0xFFFF }
        }
    }
    return $crc
}

# ---- 读取当前可读数据（限次防死循环：设备持续发采集帧，无限 recv 会永不超时）----
function Read-Available([System.Net.Sockets.NetworkStream]$stream, [byte[]]$buf, [int]$maxRecv = 40) {
    $ms = New-Object System.IO.MemoryStream
    for ($i = 0; $i -lt $maxRecv; $i++) {
        if (-not $stream.DataAvailable) { break }
        $n = $stream.Read($buf, 0, $buf.Length)
        if ($n -le 0) { break }
        $ms.Write($buf, 0, $n)
    }
    return $ms.ToArray()
}

$FAIL = $false

# ---- 1) 启动 simulator ----
Write-Host "== E2E 探针开始（$Exe @ port $Port）=="
$proc = Start-Process -FilePath $Exe -ArgumentList "-p $Port --mode normal" -PassThru
Start-Sleep -Milliseconds 1200
if ($proc.HasExited) {
    Write-Host "FAIL: simulator 启动即退出 exit=$($proc.ExitCode)"
    exit 1
}

# ---- 2) 连接，先清空采集帧让设备端就绪 ----
$client = $null
try {
    $client = New-Object System.Net.Sockets.TcpClient
    $client.ReceiveTimeout = 3000
    $client.Connect("127.0.0.1", $Port)
    $stream = $client.GetStream()
    Start-Sleep -Milliseconds 300
    $buf = New-Object byte[] 4096
    $null = Read-Available $stream $buf        # 丢弃握手期的采集帧，确保就绪

    # ---- 3) 发送配置查询帧 {AA55 03 01 0000 + CRC16 LE} ----
    $query = [byte[]](0xAA, 0x55, 0x03, 0x01, 0x00, 0x00)
    $crc   = Get-ModbusCrc -data $query[2..5]
    $frame = $query + [byte[]](($crc -band 0xFF), (($crc -shr 8) -band 0xFF))
    $stream.Write($frame, 0, $frame.Length)
    $stream.Flush()

    # ---- 4) 轮询读取：查找 0x83 配置应答帧 ----
    $gotConfig = $false
    $configInfo = ""
    for ($i = 0; $i -lt 15 -and -not $gotConfig; $i++) {
        Start-Sleep -Milliseconds 120
        $resp = Read-Available $stream $buf
        for ($j = 0; $j -lt ($resp.Length - 3); $j++) {
            if ($resp[$j] -eq 0xAA -and $resp[$j+1] -eq 0x55 -and $resp[$j+2] -eq 0x83 -and $resp[$j+3] -eq 0x01) {
                $gotConfig = $true
                $len = ($resp[$j+4] -shl 8) -bor $resp[$j+5]
                $count = if ($resp.Length -gt ($j + 7)) { $resp[$j+6] } else { -1 }
                $expectLen = 7 + 33 * $count + 2
                if ($count -lt 1 -or $len -ne (1 + 33 * $count) -or $resp.Length -lt ($j + $expectLen)) {
                    $configInfo = "✗ 配置帧结构异常 count=$count len=$len 应=$expectLen"
                    $FAIL = $true
                } else {
                    $configInfo = "✓ FUNC=83 CMD=01 count=$count LEN=$len 帧长=$expectLen 合法"
                }
                break
            }
        }
    }
    if ($gotConfig) { Write-Host "  配置应答：$configInfo" }
    else { Write-Host "  ✗ 未收到 0x83 配置应答帧"; $FAIL = $true }

    # ---- 5) 校验采集上报帧（normal 模式正弦，LEN=16）----
    $gotData = $false
    for ($i = 0; $i -lt 10 -and -not $gotData; $i++) {
        Start-Sleep -Milliseconds 150
        $data = Read-Available $stream $buf
        for ($j = 0; $j -lt ($data.Length - 3); $j++) {
            if ($data[$j] -eq 0xAA -and $data[$j+1] -eq 0x55 -and $data[$j+2] -eq 0x01 -and $data[$j+3] -eq 0x01) {
                $gotData = $true
                $len = ($data[$j+4] -shl 8) -bor $data[$j+5]
                Write-Host "  采集上报：✓ FUNC=01 CMD=01 LEN=$len（normal 正弦 payload）"
                break
            }
        }
    }
    if (-not $gotData) { Write-Host "  ✗ 未收到 0x01 采集上报帧"; $FAIL = $true }
}
catch {
    Write-Host "FAIL: 探针异常 $($_.Exception.Message)"
    $FAIL = $true
}
finally {
    if ($client) { $client.Close() }
    if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }
}

if ($FAIL) { Write-Host "== E2E FAIL =="; exit 1 }
else       { Write-Host "== E2E PASS: 发布包 simulator 协议链路完整可用 =="; exit 0 }
