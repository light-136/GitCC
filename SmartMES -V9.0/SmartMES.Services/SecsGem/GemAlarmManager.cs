// ============================================================
// 文件：GemAlarmManager.cs
// 用途：GEM 告警管理器 — 告警注册、设置/清除、列表查询
// 标准：SEMI E30 — 告警管理
// 设计思路：
//   GEM 告警管理负责：
//   1. 注册告警定义（ID、文本、严重级别）
//   2. 设置和清除告警（改变激活状态）
//   3. 启用/禁用告警的上报功能
//   4. 构建 S5F1 告警上报消息
//   5. 响应主机的告警查询（S5F5/S5F7）
// ============================================================

using SmartMES.Core.Models;

namespace SmartMES.Services.SecsGem
{
    /// <summary>
    /// 告警严重级别 — 按 SEMI 标准分级。
    /// </summary>
    public enum AlarmSeverity : byte
    {
        /// <summary>个人安全告警。</summary>
        PersonalSafety = 1,
        /// <summary>设备安全告警。</summary>
        EquipmentSafety = 2,
        /// <summary>参数控制告警（超限等）。</summary>
        ParameterControl = 3,
        /// <summary>设备状态异常告警。</summary>
        EquipmentStatus = 4,
        /// <summary>注意级别告警（提示信息）。</summary>
        Attention = 5
    }

    /// <summary>
    /// GEM 告警管理器 — 管理设备告警的完整生命周期。
    ///
    /// 对应 SECS-II 消息：
    ///   S5F1/S5F2   — 告警设置/清除 上报
    ///   S5F3/S5F4   — 启用/禁用告警上报
    ///   S5F5/S5F6   — 列出所有告警
    ///   S5F7/S5F8   — 列出已启用告警
    /// </summary>
    public class GemAlarmManager
    {
        // 所有注册的告警定义
        private readonly Dictionary<uint, SecsAlarm> _alarms = new();
        private readonly object _lock = new();

        /// <summary>告警状态变更事件（告警被设置或清除时触发）。</summary>
        public event EventHandler<SecsAlarm>? AlarmStateChanged;

        /// <summary>日志事件。</summary>
        public event EventHandler<string>? MessageLogged;

        // ========== 告警注册 ==========

        /// <summary>
        /// 注册告警定义。
        /// </summary>
        /// <param name="alarm">告警对象（包含 ID、文本、严重级别等）。</param>
        public void RegisterAlarm(SecsAlarm alarm)
        {
            lock (_lock)
            {
                _alarms[alarm.Id] = alarm;
                Log($"[GEM告警] 注册：ID={alarm.Id}, 文本={alarm.Text}");
            }
        }

        /// <summary>
        /// 批量注册告警。
        /// </summary>
        public void RegisterAlarms(IEnumerable<SecsAlarm> alarms)
        {
            foreach (var alarm in alarms)
                RegisterAlarm(alarm);
        }

        // ========== 告警设置/清除 ==========

        /// <summary>
        /// 设置告警（激活告警）。
        /// 设置后应发送 S5F1 上报主机。
        /// </summary>
        /// <param name="alarmId">告警 ID。</param>
        /// <returns>true=成功, false=告警ID不存在。</returns>
        public bool SetAlarm(uint alarmId)
        {
            lock (_lock)
            {
                if (!_alarms.TryGetValue(alarmId, out var alarm))
                    return false;

                if (alarm.IsSet)
                    return true; // 已经激活

                alarm.IsSet = true;
                Log($"[GEM告警] 设置：ID={alarmId}, {alarm.Text}");
                AlarmStateChanged?.Invoke(this, alarm);
                return true;
            }
        }

        /// <summary>
        /// 清除告警（取消激活）。
        /// 清除后应发送 S5F1 上报主机。
        /// </summary>
        public bool ClearAlarm(uint alarmId)
        {
            lock (_lock)
            {
                if (!_alarms.TryGetValue(alarmId, out var alarm))
                    return false;

                if (!alarm.IsSet)
                    return true;

                alarm.IsSet = false;
                Log($"[GEM告警] 清除：ID={alarmId}, {alarm.Text}");
                AlarmStateChanged?.Invoke(this, alarm);
                return true;
            }
        }

        /// <summary>
        /// 清除所有活跃告警。
        /// </summary>
        public void ClearAllAlarms()
        {
            lock (_lock)
            {
                foreach (var alarm in _alarms.Values.Where(a => a.IsSet))
                {
                    alarm.IsSet = false;
                    AlarmStateChanged?.Invoke(this, alarm);
                }
                Log("[GEM告警] 清除所有告警");
            }
        }

        // ========== 告警启用/禁用 ==========

        /// <summary>
        /// 启用或禁用告警的上报功能（S5F3 命令）。
        /// </summary>
        public bool SetAlarmEnabled(uint alarmId, bool enabled)
        {
            lock (_lock)
            {
                if (!_alarms.TryGetValue(alarmId, out var alarm))
                    return false;

                alarm.IsEnabled = enabled;
                Log($"[GEM告警] ID={alarmId} {(enabled ? "启用" : "禁用")}上报");
                return true;
            }
        }

        // ========== 告警查询 ==========

        /// <summary>
        /// 获取所有告警（S5F5/S5F6）。
        /// </summary>
        public List<SecsAlarm> GetAllAlarms()
        {
            lock (_lock) { return _alarms.Values.ToList(); }
        }

        /// <summary>
        /// 获取所有已启用的告警（S5F7/S5F8）。
        /// </summary>
        public List<SecsAlarm> GetEnabledAlarms()
        {
            lock (_lock)
            {
                return _alarms.Values.Where(a => a.IsEnabled).ToList();
            }
        }

        /// <summary>
        /// 获取所有活跃（已触发）的告警。
        /// </summary>
        public List<SecsAlarm> GetActiveAlarms()
        {
            lock (_lock)
            {
                return _alarms.Values.Where(a => a.IsSet).ToList();
            }
        }

        /// <summary>
        /// 获取指定告警。
        /// </summary>
        public SecsAlarm? GetAlarm(uint alarmId)
        {
            lock (_lock)
            {
                return _alarms.GetValueOrDefault(alarmId);
            }
        }

        // ========== S5F1 消息构建 ==========

        /// <summary>
        /// 构建 S5F1 告警上报消息体。
        /// 消息格式：
        ///   &lt;L [3]
        ///     &lt;B ALCD&gt;     ; 告警码（bit7=设置/清除, bit0-2=类别）
        ///     &lt;U4 ALID&gt;    ; 告警 ID
        ///     &lt;A ALTX&gt;     ; 告警文本
        ///   &gt;
        /// </summary>
        /// <param name="alarmId">告警 ID。</param>
        /// <param name="isSet">true=设置告警, false=清除告警。</param>
        public SecsItem? BuildAlarmReport(uint alarmId, bool isSet)
        {
            lock (_lock)
            {
                if (!_alarms.TryGetValue(alarmId, out var alarm))
                    return null;

                if (!alarm.IsEnabled)
                    return null;

                var root = SecsItem.CreateList();

                // ALCD：告警码，bit7 表示设置(1)或清除(0)
                byte alcd = (byte)(isSet ? 0x80 : 0x00);
                root.Children.Add(SecsItem.CreateBinary(new[] { alcd }));

                // ALID：告警 ID
                root.Children.Add(SecsItem.CreateU4(alarmId));

                // ALTX：告警文本
                root.Children.Add(SecsItem.CreateAscii(alarm.Text ?? ""));

                return root;
            }
        }

        /// <summary>
        /// 构建 S5F6 响应（告警列表）。
        /// </summary>
        public SecsItem BuildAlarmListResponse(List<uint>? alarmIds = null)
        {
            lock (_lock)
            {
                var alarms = alarmIds == null || alarmIds.Count == 0
                    ? _alarms.Values.ToList()
                    : alarmIds.Where(_alarms.ContainsKey).Select(id => _alarms[id]).ToList();

                var root = SecsItem.CreateList();
                foreach (var alarm in alarms)
                {
                    var item = SecsItem.CreateList();
                    byte alcd = (byte)(alarm.IsSet ? 0x80 : 0x00);
                    item.Children.Add(SecsItem.CreateBinary(new[] { alcd }));
                    item.Children.Add(SecsItem.CreateU4(alarm.Id));
                    item.Children.Add(SecsItem.CreateAscii(alarm.Text ?? ""));
                    root.Children.Add(item);
                }

                return root;
            }
        }

        private void Log(string msg) => MessageLogged?.Invoke(this, msg);
    }
}
