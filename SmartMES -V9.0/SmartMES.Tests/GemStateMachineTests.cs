// ============================================================
// 文件：GemStateMachineTests.cs
// 用途：GEM 双层状态机单元测试
// 测试目标：SmartMES.Services.SecsGem.GemStateMachine
// 设计思路：
//   验证 GEM 状态机的通信状态和控制状态转换是否符合 SEMI E30 标准。
//   测试覆盖：
//     - 初始状态验证
//     - 合法的状态转换序列（启用、通信建立、上线、离线、禁用）
//     - 主机拒绝上线的处理
//     - 通信丢失的恢复
//     - 非法状态转换的拒绝
// ============================================================

using SmartMES.Core.Models;
using SmartMES.Services.SecsGem;

namespace SmartMES.Tests
{
    /// <summary>
    /// GemStateMachine 双层状态机单元测试类。
    /// 验证通信状态机和控制状态机的转换逻辑是否正确。
    /// </summary>
    public class GemStateMachineTests
    {
        // ===== 测试1：初始状态应为 Disabled + EquipmentOffline =====

        [Fact]
        public void InitialState_新建状态机应处于Disabled和EquipmentOffline()
        {
            // 准备 & 执行：创建新的 GEM 状态机实例
            var sm = new GemStateMachine();

            // 验证：通信状态应为 Disabled（通信功能未启用）
            Assert.Equal(GemCommunicationState.Disabled, sm.CommunicationState);

            // 验证：控制状态应为 EquipmentOffline（设备离线）
            Assert.Equal(GemControlState.EquipmentOffline, sm.ControlState);
        }

        // ===== 测试2：Enable 应将通信状态转为 WaitCommunicating =====

        [Fact]
        public void Enable_从Disabled调用应转换为WaitCommunicating()
        {
            // 准备：创建状态机（初始状态为 Disabled）
            var sm = new GemStateMachine();

            // 执行：启用通信
            bool result = sm.Enable();

            // 验证：操作成功
            Assert.True(result);

            // 验证：通信状态变为 WaitCommunicating（等待建立通信）
            Assert.Equal(GemCommunicationState.WaitCommunicating, sm.CommunicationState);
        }

        // ===== 测试3：完整上线序列 Enable → CommunicationEstablished → RequestOnline → OnlineAccepted =====

        [Fact]
        public void FullOnlineSequence_完整上线流程应最终到达OnlineRemote()
        {
            // 准备：创建状态机
            var sm = new GemStateMachine();

            // 执行步骤1：启用通信（Disabled → WaitCommunicating）
            bool enableOk = sm.Enable();
            Assert.True(enableOk);
            Assert.Equal(GemCommunicationState.WaitCommunicating, sm.CommunicationState);

            // 执行步骤2：通信建立成功（WaitCommunicating → Communicating）
            bool commOk = sm.CommunicationEstablished();
            Assert.True(commOk);
            Assert.Equal(GemCommunicationState.Communicating, sm.CommunicationState);

            // 执行步骤3：请求上线（EquipmentOffline → AttemptOnline）
            bool reqOk = sm.RequestOnline();
            Assert.True(reqOk);
            Assert.Equal(GemControlState.AttemptOnline, sm.ControlState);

            // 执行步骤4：主机接受上线（AttemptOnline → OnlineRemote）
            bool acceptOk = sm.OnlineAccepted();
            Assert.True(acceptOk);
            Assert.Equal(GemControlState.OnlineRemote, sm.ControlState);
        }

        // ===== 测试4：主机拒绝上线，控制状态应回退到 EquipmentOffline =====

        [Fact]
        public void OnlineRejected_主机拒绝后应回退到EquipmentOffline()
        {
            // 准备：执行到 AttemptOnline 状态
            var sm = new GemStateMachine();
            sm.Enable();
            sm.CommunicationEstablished();
            sm.RequestOnline();

            // 前置验证：当前应处于 AttemptOnline 状态
            Assert.Equal(GemControlState.AttemptOnline, sm.ControlState);

            // 执行：主机拒绝上线请求
            bool result = sm.OnlineRejected();

            // 验证：操作成功，控制状态回退到 EquipmentOffline
            Assert.True(result);
            Assert.Equal(GemControlState.EquipmentOffline, sm.ControlState);
        }

        // ===== 测试5：从在线状态执行 GoOffline =====

        [Fact]
        public void GoOffline_从OnlineRemote应成功转为EquipmentOffline()
        {
            // 准备：执行完整上线流程，到达 OnlineRemote 状态
            var sm = new GemStateMachine();
            sm.Enable();
            sm.CommunicationEstablished();
            sm.RequestOnline();
            sm.OnlineAccepted();

            // 前置验证：当前应处于 OnlineRemote 状态
            Assert.Equal(GemControlState.OnlineRemote, sm.ControlState);

            // 执行：请求离线
            bool result = sm.GoOffline();

            // 验证：操作成功，控制状态变为 EquipmentOffline
            Assert.True(result);
            Assert.Equal(GemControlState.EquipmentOffline, sm.ControlState);
        }

        // ===== 测试6：Disable 应从任意状态重置为 Disabled + EquipmentOffline =====

        [Fact]
        public void Disable_从OnlineRemote应重置为Disabled和EquipmentOffline()
        {
            // 准备：执行完整上线流程，到达 OnlineRemote 状态
            var sm = new GemStateMachine();
            sm.Enable();
            sm.CommunicationEstablished();
            sm.RequestOnline();
            sm.OnlineAccepted();

            // 前置验证：当前在线
            Assert.Equal(GemCommunicationState.Communicating, sm.CommunicationState);
            Assert.Equal(GemControlState.OnlineRemote, sm.ControlState);

            // 执行：禁用通信
            bool result = sm.Disable();

            // 验证：操作成功，通信和控制状态均重置
            Assert.True(result);
            Assert.Equal(GemCommunicationState.Disabled, sm.CommunicationState);
            Assert.Equal(GemControlState.EquipmentOffline, sm.ControlState);
        }

        // ===== 测试7：CommunicationLost 应正确处理通信丢失 =====

        [Fact]
        public void CommunicationLost_从Communicating应回退到WaitCommunicating并离线()
        {
            // 准备：执行完整上线流程
            var sm = new GemStateMachine();
            sm.Enable();
            sm.CommunicationEstablished();
            sm.RequestOnline();
            sm.OnlineAccepted();

            // 前置验证：当前在线通信中
            Assert.Equal(GemCommunicationState.Communicating, sm.CommunicationState);
            Assert.Equal(GemControlState.OnlineRemote, sm.ControlState);

            // 执行：模拟通信丢失
            bool result = sm.CommunicationLost();

            // 验证：通信状态回退到 WaitCommunicating
            Assert.True(result);
            Assert.Equal(GemCommunicationState.WaitCommunicating, sm.CommunicationState);

            // 验证：控制状态也应重置为 EquipmentOffline（通信断了，无法保持在线）
            Assert.Equal(GemControlState.EquipmentOffline, sm.ControlState);
        }

        // ===== 测试8：非法状态转换应被拒绝，状态不变 =====

        [Fact]
        public void InvalidTransition_非AttemptOnline时调用OnlineAccepted应被拒绝()
        {
            // 准备：创建状态机，通信已建立但还未请求上线
            var sm = new GemStateMachine();
            sm.Enable();
            sm.CommunicationEstablished();

            // 前置验证：控制状态为 EquipmentOffline（还未请求上线）
            Assert.Equal(GemControlState.EquipmentOffline, sm.ControlState);

            // 执行：在未处于 AttemptOnline 状态时直接调用 OnlineAccepted
            bool result = sm.OnlineAccepted();

            // 验证：操作被拒绝（返回 false）
            Assert.False(result);

            // 验证：状态保持不变（仍为 EquipmentOffline）
            Assert.Equal(GemControlState.EquipmentOffline, sm.ControlState);
        }
    }
}
