// 固件升级引擎
// 实现完整的固件升级流程状态机
//
// 升级流程（4步）：
// Step1: 发送升级请求(0x0010) → 等待驱动器回复"处于Boot中，具备升级条件"
// Step2: 发送启动升级(0x0011) → 驱动器擦除Flash → 等待"擦除完成"回复
// Step3: 循环发送升级数据(0x0012) → 每帧≤128字节 → 等待每帧确认
// Step4: 发送完成升级(0x0013) → 驱动器从Boot跳转App

using GD32FirmwareUpdater.Protocol;

namespace GD32FirmwareUpdater.Services
{
    /// <summary>
    /// 升级流程状态枚举
    /// </summary>
    public enum UpgradeState
    {
        Idle,           // 空闲
        Requesting,     // 正在发送升级请求
        Erasing,        // 正在擦除Flash
        Transferring,   // 正在传输数据
        Finishing,      // 正在完成升级（跳转App）
        Completed,      // 升级完成
        Failed          // 升级失败
    }

    /// <summary>
    /// 升级进度报告
    /// </summary>
    public class UpgradeProgress
    {
        public UpgradeState State { get; set; }
        public int Percentage { get; set; }
        public string Message { get; set; } = string.Empty;
        public int SentBytes { get; set; }
        public int TotalBytes { get; set; }
    }

    /// <summary>
    /// 固件升级引擎
    /// 管理完整的升级流程，提供进度报告和取消支持
    /// </summary>
    public class FirmwareUpgradeService
    {
        private readonly SerialPortService _serialPort;

        // 通讯超时时间（毫秒）
        private const int RESPONSE_TIMEOUT = 5000;

        // 擦除Flash超时（擦除操作较慢，需要更长等待）
        private const int ERASE_TIMEOUT = 30000;

        // 最大重试次数
        private const int MAX_RETRY = 3;

        // 进度报告事件
        public event Action<UpgradeProgress>? OnProgress;

        // 日志事件
        public event Action<string>? OnLog;

        public FirmwareUpgradeService(SerialPortService serialPort)
        {
            _serialPort = serialPort;
        }

        /// <summary>
        /// 执行完整的固件升级流程
        /// </summary>
        /// <param name="firmwareData">固件二进制数据</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>升级是否成功</returns>
        public async Task<bool> ExecuteUpgradeAsync(byte[] firmwareData, CancellationToken cancellationToken = default)
        {
            try
            {
                // === Step 1: 发送升级请求 ===
                ReportProgress(UpgradeState.Requesting, 0, "正在发送升级请求...");
                Log("Step 1/4: 发送升级请求 (CMD=0x0010)");

                bool requestOk = await SendUpgradeRequestAsync(cancellationToken);
                if (!requestOk)
                {
                    ReportProgress(UpgradeState.Failed, 0, "升级请求失败：设备未响应或不具备升级条件");
                    return false;
                }

                cancellationToken.ThrowIfCancellationRequested();

                // === Step 2: 启动升级（擦除Flash） ===
                ReportProgress(UpgradeState.Erasing, 5, "正在擦除Flash，请等待...");
                Log($"Step 2/4: 启动升级 (CMD=0x0011)，固件大小: {firmwareData.Length} 字节");

                bool eraseOk = await SendUpgradeStartAsync((uint)firmwareData.Length, cancellationToken);
                if (!eraseOk)
                {
                    ReportProgress(UpgradeState.Failed, 5, "Flash擦除失败");
                    return false;
                }

                cancellationToken.ThrowIfCancellationRequested();

                // === Step 3: 发送升级数据 ===
                ReportProgress(UpgradeState.Transferring, 10, "正在传输固件数据...");
                Log($"Step 3/4: 发送升级数据 (CMD=0x0012)，共 {firmwareData.Length} 字节");

                bool transferOk = await SendUpgradeDataAsync(firmwareData, cancellationToken);
                if (!transferOk)
                {
                    ReportProgress(UpgradeState.Failed, 0, "数据传输失败");
                    return false;
                }

                cancellationToken.ThrowIfCancellationRequested();

                // === Step 4: 完成升级（跳转App） ===
                ReportProgress(UpgradeState.Finishing, 95, "正在完成升级，跳转App...");
                Log("Step 4/4: 发送完成升级指令 (CMD=0x0013)");

                bool jumpOk = await SendJumpAppAsync(cancellationToken);
                if (!jumpOk)
                {
                    ReportProgress(UpgradeState.Failed, 95, "跳转App失败");
                    return false;
                }

                ReportProgress(UpgradeState.Completed, 100, "固件升级成功！");
                Log("固件升级完成！");
                return true;
            }
            catch (OperationCanceledException)
            {
                ReportProgress(UpgradeState.Failed, 0, "升级已取消");
                Log("用户取消了升级操作");
                return false;
            }
            catch (Exception ex)
            {
                ReportProgress(UpgradeState.Failed, 0, $"升级异常: {ex.Message}");
                Log($"升级过程发生异常: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 查询Bootloader版本号
        /// </summary>
        /// <returns>版本号字符串，失败返回null</returns>
        public async Task<string?> QueryVersionAsync()
        {
            Log("查询Bootloader版本号 (CMD=0x0014)");
            byte[] frame = FrameBuilder.BuildVersionRequest();
            var response = await _serialPort.SendAndWaitResponseAsync(frame, RESPONSE_TIMEOUT);

            if (response == null)
            {
                Log("版本号查询超时");
                return null;
            }

            if (response.Cmd != CommandCode.VER_REQ || response.Data.Length < 5)
            {
                Log("版本号回复格式异常");
                return null;
            }

            // 版本号由5个字节组成
            string version = $"{response.Data[0]}.{response.Data[1]}.{response.Data[2]}.{response.Data[3]}.{response.Data[4]}";
            Log($"Bootloader版本号: {version}");
            return version;
        }

        /// <summary>
        /// Step1: 发送升级请求并处理回复
        /// 如果设备在App中会跳转到Boot，需要等待后重试
        /// </summary>
        private async Task<bool> SendUpgradeRequestAsync(CancellationToken ct)
        {
            for (int retry = 0; retry < MAX_RETRY; retry++)
            {
                ct.ThrowIfCancellationRequested();

                byte[] frame = FrameBuilder.BuildUpgradeRequest(UpgradeRequestType.REQUEST_UPGRADE);
                var response = await _serialPort.SendAndWaitResponseAsync(frame, RESPONSE_TIMEOUT);

                if (response == null)
                {
                    Log($"升级请求超时（第{retry + 1}次）");
                    continue;
                }

                if (response.Cmd != CommandCode.UPGRADE_REQ || response.Data.Length < 1)
                {
                    Log("升级请求回复格式异常");
                    continue;
                }

                byte status = response.Data[0];
                switch (status)
                {
                    case UpgradeRequestResponse.IN_BOOT_READY:
                        Log("设备已在Boot模式，具备升级条件");
                        return true;

                    case UpgradeRequestResponse.IN_APP_JUMPING:
                        Log("设备正在从App跳转到Boot，等待2秒后重试...");
                        await Task.Delay(2000, ct);
                        continue;

                    case UpgradeRequestResponse.IN_APP_STATUS_ONLY:
                        Log("设备在App模式，仅回复状态。重新发送升级请求...");
                        continue;

                    case UpgradeRequestResponse.IN_BOOT_STATUS_ONLY:
                        Log("设备在Boot模式（仅状态回复），尝试继续...");
                        return true;

                    default:
                        Log($"未知的回复状态: 0x{status:X2}");
                        continue;
                }
            }

            return false;
        }

        /// <summary>
        /// Step2: 发送启动升级指令，等待Flash擦除完成
        /// </summary>
        private async Task<bool> SendUpgradeStartAsync(uint fileSize, CancellationToken ct)
        {
            byte[] frame = FrameBuilder.BuildUpgradeStart(fileSize);
            // 擦除Flash需要较长时间，使用更长的超时
            var response = await _serialPort.SendAndWaitResponseAsync(frame, ERASE_TIMEOUT);

            if (response == null)
            {
                Log("启动升级超时（Flash擦除可能需要较长时间）");
                return false;
            }

            if (response.Cmd != CommandCode.UPGRADE_START || response.Data.Length < 1)
            {
                Log("启动升级回复格式异常");
                return false;
            }

            if (response.Data[0] == ResponseStatus.SUCCESS)
            {
                Log("Flash擦除完成，准备接收固件数据");
                return true;
            }
            else
            {
                Log($"Flash擦除失败，错误码: 0x{response.Data[0]:X2}");
                return false;
            }
        }

        /// <summary>
        /// Step3: 分包发送固件数据
        /// 每包最大128字节，等待每帧确认后再发送下一帧
        /// </summary>
        private async Task<bool> SendUpgradeDataAsync(byte[] firmwareData, CancellationToken ct)
        {
            int totalSize = firmwareData.Length;
            int offset = 0;
            int packetIndex = 0;
            int totalPackets = (totalSize + FrameConstants.MAX_DATA_PAYLOAD - 1) / FrameConstants.MAX_DATA_PAYLOAD;

            while (offset < totalSize)
            {
                ct.ThrowIfCancellationRequested();

                // 计算本帧数据大小（最后一帧可能不足128字节）
                int chunkSize = Math.Min(FrameConstants.MAX_DATA_PAYLOAD, totalSize - offset);
                byte[] chunk = new byte[chunkSize];
                Array.Copy(firmwareData, offset, chunk, 0, chunkSize);

                // 构建并发送数据帧
                byte[] frame = FrameBuilder.BuildUpgradeData((uint)offset, chunk);

                bool sentOk = false;
                for (int retry = 0; retry < MAX_RETRY; retry++)
                {
                    var response = await _serialPort.SendAndWaitResponseAsync(frame, RESPONSE_TIMEOUT);

                    if (response == null)
                    {
                        Log($"数据包 {packetIndex + 1}/{totalPackets} 超时（第{retry + 1}次重试）");
                        continue;
                    }

                    if (response.Cmd != CommandCode.UPGRADE_DATA || response.Data.Length < 1)
                    {
                        Log($"数据包 {packetIndex + 1}/{totalPackets} 回复格式异常");
                        continue;
                    }

                    if (response.Data[0] == ResponseStatus.SUCCESS)
                    {
                        sentOk = true;
                        break;
                    }
                    else
                    {
                        Log($"数据包 {packetIndex + 1}/{totalPackets} 写入失败，重试中...");
                    }
                }

                if (!sentOk)
                {
                    Log($"数据包 {packetIndex + 1}/{totalPackets} 发送失败（已重试{MAX_RETRY}次）");
                    return false;
                }

                offset += chunkSize;
                packetIndex++;

                // 计算进度：数据传输阶段占10%-95%
                int progressPercent = 10 + (int)(85.0 * offset / totalSize);
                ReportProgress(UpgradeState.Transferring, progressPercent,
                    $"正在传输: {offset}/{totalSize} 字节 ({packetIndex}/{totalPackets} 包)");
            }

            Log($"数据传输完成: 共发送 {totalPackets} 个数据包，{totalSize} 字节");
            return true;
        }

        /// <summary>
        /// Step4: 发送完成升级指令，驱动器跳转App
        /// </summary>
        private async Task<bool> SendJumpAppAsync(CancellationToken ct)
        {
            byte[] frame = FrameBuilder.BuildJumpApp();
            var response = await _serialPort.SendAndWaitResponseAsync(frame, RESPONSE_TIMEOUT);

            if (response == null)
            {
                Log("完成升级指令超时");
                return false;
            }

            if (response.Cmd != CommandCode.JUMP_APP || response.Data.Length < 1)
            {
                Log("完成升级回复格式异常");
                return false;
            }

            if (response.Data[0] == ResponseStatus.SUCCESS)
            {
                Log("驱动器已成功跳转到App，升级完成");
                return true;
            }
            else
            {
                Log($"跳转App失败，错误码: 0x{response.Data[0]:X2}");
                return false;
            }
        }

        /// <summary>
        /// 报告升级进度
        /// </summary>
        private void ReportProgress(UpgradeState state, int percentage, string message)
        {
            OnProgress?.Invoke(new UpgradeProgress
            {
                State = state,
                Percentage = percentage,
                Message = message
            });
        }

        private void Log(string message)
        {
            OnLog?.Invoke($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
        }
    }
}
