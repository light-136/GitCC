// ============================================================
// 文件：GemStateMachine.cs
// 用途：GEM 双层状态机 — 通信状态 + 控制状态（SEMI E30）
// 标准：SEMI E30 — GEM (Generic Equipment Model)
// 设计思路：
//   GEM 规范定义了两层状态机：
//   1. 通信状态机：Disabled → WaitCommunicating → Communicating
//   2. 控制状态机：EquipmentOffline → AttemptOnline → OnlineRemote/Local
//   状态转换有严格的守卫条件，非法转换会被拒绝。
//   每次状态变更都会触发事件，供上层编排服务和UI响应。
// ============================================================

using SmartMES.Core.Models;

namespace SmartMES.Services.SecsGem
{
    /// <summary>
    /// GEM 双层状态机 — 管理通信状态和控制状态的转换。
    ///
    /// 通信状态模型：
    ///   Disabled ─(Enable)→ WaitCommunicating ─(S1F13成功)→ Communicating
    ///   任意状态 ─(Disable)→ Disabled
    ///   Communicating ─(通信失败)→ WaitCommunicating
    ///
    /// 控制状态模型：
    ///   EquipmentOffline ─(请求上线)→ AttemptOnline
    ///   AttemptOnline ─(主机接受)→ OnlineRemote
    ///   AttemptOnline ─(主机拒绝)→ EquipmentOffline
    ///   OnlineRemote ↔ OnlineLocal（操作员切换）
    ///   Online* ─(请求离线)→ EquipmentOffline
    /// </summary>
    public class GemStateMachine
    {
        private readonly object _lock = new();

        /// <summary>当前通信状态。</summary>
        public GemCommunicationState CommunicationState { get; private set; }
            = GemCommunicationState.Disabled;

        /// <summary>当前控制状态。</summary>
        public GemControlState ControlState { get; private set; }
            = GemControlState.EquipmentOffline;

        /// <summary>通信状态变更事件。</summary>
        public event EventHandler<GemCommunicationState>? CommunicationStateChanged;

        /// <summary>控制状态变更事件。</summary>
        public event EventHandler<GemControlState>? ControlStateChanged;

        /// <summary>日志事件。</summary>
        public event EventHandler<string>? MessageLogged;

        // ========== 通信状态转换 ==========

        /// <summary>
        /// 启用通信 — Disabled → WaitCommunicating。
        /// 设备启用后开始等待主机的通信建立消息（S1F13）。
        /// </summary>
        public bool Enable()
        {
            lock (_lock)
            {
                if (CommunicationState != GemCommunicationState.Disabled)
                {
                    Log($"[GEM状态机] Enable 拒绝：当前状态 {CommunicationState}");
                    return false;
                }

                SetCommunicationState(GemCommunicationState.WaitCommunicating);
                Log("[GEM状态机] 通信状态：Disabled → WaitCommunicating");
                return true;
            }
        }

        /// <summary>
        /// 禁用通信 — 任意状态 → Disabled。
        /// 同时将控制状态重置为 EquipmentOffline。
        /// </summary>
        public bool Disable()
        {
            lock (_lock)
            {
                SetCommunicationState(GemCommunicationState.Disabled);
                SetControlState(GemControlState.EquipmentOffline);
                Log("[GEM状态机] 通信已禁用，状态 → Disabled");
                return true;
            }
        }

        /// <summary>
        /// 通信建立成功 — WaitCommunicating → Communicating。
        /// 在 S1F13/S1F14 握手成功后调用。
        /// </summary>
        public bool CommunicationEstablished()
        {
            lock (_lock)
            {
                if (CommunicationState != GemCommunicationState.WaitCommunicating)
                {
                    Log($"[GEM状态机] 通信建立拒绝：当前状态 {CommunicationState}");
                    return false;
                }

                SetCommunicationState(GemCommunicationState.Communicating);
                Log("[GEM状态机] 通信状态：WaitCommunicating → Communicating");
                return true;
            }
        }

        /// <summary>
        /// 通信失败 — Communicating → WaitCommunicating。
        /// 在通信链路断开或超时时调用。
        /// </summary>
        public bool CommunicationLost()
        {
            lock (_lock)
            {
                if (CommunicationState != GemCommunicationState.Communicating)
                    return false;

                SetCommunicationState(GemCommunicationState.WaitCommunicating);
                SetControlState(GemControlState.EquipmentOffline);
                Log("[GEM状态机] 通信丢失：Communicating → WaitCommunicating");
                return true;
            }
        }

        // ========== 控制状态转换 ==========

        /// <summary>
        /// 请求上线 — EquipmentOffline → AttemptOnline。
        /// 设备向主机发送 S1F17 请求上线。
        /// </summary>
        public bool RequestOnline()
        {
            lock (_lock)
            {
                if (CommunicationState != GemCommunicationState.Communicating)
                {
                    Log("[GEM状态机] 请求上线拒绝：通信未建立");
                    return false;
                }

                if (ControlState != GemControlState.EquipmentOffline)
                {
                    Log($"[GEM状态机] 请求上线拒绝：当前控制状态 {ControlState}");
                    return false;
                }

                SetControlState(GemControlState.AttemptOnline);
                Log("[GEM状态机] 控制状态：EquipmentOffline → AttemptOnline");
                return true;
            }
        }

        /// <summary>
        /// 上线成功（主机接受） — AttemptOnline → OnlineRemote。
        /// </summary>
        public bool OnlineAccepted()
        {
            lock (_lock)
            {
                if (ControlState != GemControlState.AttemptOnline)
                {
                    Log($"[GEM状态机] 上线接受拒绝：当前状态 {ControlState}");
                    return false;
                }

                SetControlState(GemControlState.OnlineRemote);
                Log("[GEM状态机] 控制状态：AttemptOnline → OnlineRemote");
                return true;
            }
        }

        /// <summary>
        /// 上线失败（主机拒绝） — AttemptOnline → EquipmentOffline。
        /// </summary>
        public bool OnlineRejected()
        {
            lock (_lock)
            {
                if (ControlState != GemControlState.AttemptOnline)
                    return false;

                SetControlState(GemControlState.EquipmentOffline);
                Log("[GEM状态机] 控制状态：AttemptOnline → EquipmentOffline（主机拒绝）");
                return true;
            }
        }

        /// <summary>
        /// 切换到本地控制 — OnlineRemote → OnlineLocal。
        /// 操作员在设备端手动切换。
        /// </summary>
        public bool SwitchToLocal()
        {
            lock (_lock)
            {
                if (ControlState != GemControlState.OnlineRemote)
                {
                    Log($"[GEM状态机] 切换本地拒绝：当前状态 {ControlState}");
                    return false;
                }

                SetControlState(GemControlState.OnlineLocal);
                Log("[GEM状态机] 控制状态：OnlineRemote → OnlineLocal");
                return true;
            }
        }

        /// <summary>
        /// 切换到远程控制 — OnlineLocal → OnlineRemote。
        /// </summary>
        public bool SwitchToRemote()
        {
            lock (_lock)
            {
                if (ControlState != GemControlState.OnlineLocal)
                {
                    Log($"[GEM状态机] 切换远程拒绝：当前状态 {ControlState}");
                    return false;
                }

                SetControlState(GemControlState.OnlineRemote);
                Log("[GEM状态机] 控制状态：OnlineLocal → OnlineRemote");
                return true;
            }
        }

        /// <summary>
        /// 请求离线 — OnlineRemote/OnlineLocal → EquipmentOffline。
        /// 主机发送 S1F15 或设备主动离线。
        /// </summary>
        public bool GoOffline()
        {
            lock (_lock)
            {
                if (ControlState != GemControlState.OnlineRemote &&
                    ControlState != GemControlState.OnlineLocal)
                {
                    Log($"[GEM状态机] 离线拒绝：当前状态 {ControlState}");
                    return false;
                }

                SetControlState(GemControlState.EquipmentOffline);
                Log("[GEM状态机] 控制状态 → EquipmentOffline");
                return true;
            }
        }

        /// <summary>
        /// 检查是否在线（Remote 或 Local）。
        /// </summary>
        public bool IsOnline => ControlState is GemControlState.OnlineRemote
                                             or GemControlState.OnlineLocal;

        // ========== 内部方法 ==========

        private void SetCommunicationState(GemCommunicationState state)
        {
            CommunicationState = state;
            CommunicationStateChanged?.Invoke(this, state);
        }

        private void SetControlState(GemControlState state)
        {
            ControlState = state;
            ControlStateChanged?.Invoke(this, state);
        }

        private void Log(string msg) => MessageLogged?.Invoke(this, msg);
    }
}
