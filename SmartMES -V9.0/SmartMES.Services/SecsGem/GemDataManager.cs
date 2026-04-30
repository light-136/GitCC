// ============================================================
// 文件：GemDataManager.cs
// 用途：GEM 数据管理器 — SV/EC/CE/报告的注册、查询与管理
// 标准：SEMI E30 — 状态变量(SV)、设备常量(EC)、采集事件(CE)、报告
// 设计思路：
//   GEM 规范定义了四类数据：
//   1. 状态变量(SV)：设备的实时状态（如温度、位置）
//   2. 设备常量(EC)：设备参数（如速度上限、偏移量）
//   3. 采集事件(CE)：设备事件（如加工完成、告警触发）
//   4. 报告定义(Report)：由SV组成的报告模板，关联到CE
//   数据管理器负责存储和查询这些数据，响应主机的查询消息。
// ============================================================

using SmartMES.Core.Models;

namespace SmartMES.Services.SecsGem
{
    /// <summary>
    /// GEM 数据管理器 — 管理状态变量(SV)、设备常量(EC)、采集事件(CE)、报告定义。
    ///
    /// 对应的 SECS-II 消息：
    ///   S1F3/S1F4   — 查询 SV 值
    ///   S1F11/S1F12 — 查询 SV 名称列表
    ///   S2F13/S2F14 — 查询 EC 值
    ///   S2F15/S2F16 — 设置 EC 值
    ///   S2F29/S2F30 — 查询 EC 名称列表
    ///   S2F33/S2F34 — 定义报告
    ///   S2F35/S2F36 — 关联事件与报告
    ///   S2F37/S2F38 — 启用/禁用事件上报
    ///   S6F11/S6F12 — 事件上报
    /// </summary>
    public class GemDataManager
    {
        // 存储容器
        private readonly Dictionary<uint, StatusVariable> _statusVariables = new();
        private readonly Dictionary<uint, EquipmentConstant> _equipmentConstants = new();
        private readonly Dictionary<uint, CollectionEvent> _collectionEvents = new();
        private readonly Dictionary<uint, ReportDefinition> _reportDefinitions = new();

        // 事件与报告的关联：事件ID → 报告ID列表
        private readonly Dictionary<uint, List<uint>> _eventReportLinks = new();

        private readonly object _lock = new();

        /// <summary>日志事件。</summary>
        public event EventHandler<string>? MessageLogged;

        // ========== 状态变量(SV) ==========

        /// <summary>
        /// 注册状态变量。
        /// </summary>
        public void RegisterStatusVariable(StatusVariable sv)
        {
            lock (_lock)
            {
                _statusVariables[sv.Id] = sv;
                Log($"[GEM数据] 注册SV：ID={sv.Id}, 名称={sv.Name}");
            }
        }

        /// <summary>
        /// 获取状态变量值（通过值回调获取实时值）。
        /// </summary>
        public object? GetStatusVariableValue(uint svId)
        {
            lock (_lock)
            {
                if (_statusVariables.TryGetValue(svId, out var sv))
                {
                    // 如果有值回调，调用它获取实时值
                    return sv.Value;
                }
                return null;
            }
        }

        /// <summary>
        /// 设置状态变量值。
        /// </summary>
        public void SetStatusVariableValue(uint svId, object value)
        {
            lock (_lock)
            {
                if (_statusVariables.TryGetValue(svId, out var sv))
                {
                    sv.Value = value;
                }
            }
        }

        /// <summary>
        /// 获取所有状态变量定义（用于 S1F11/S1F12 响应）。
        /// </summary>
        public List<StatusVariable> GetAllStatusVariables()
        {
            lock (_lock) { return _statusVariables.Values.ToList(); }
        }

        /// <summary>
        /// 构建 S1F4 响应的数据 — 按请求的 SV ID 列表返回值。
        /// </summary>
        public SecsItem BuildSvResponse(List<uint> svIds)
        {
            var list = SecsItem.CreateList();
            foreach (var id in svIds)
            {
                var value = GetStatusVariableValue(id);
                if (value is int intVal)
                    list.Children.Add(SecsItem.CreateI4(intVal));
                else if (value is double dblVal)
                    list.Children.Add(SecsItem.CreateF8(dblVal));
                else if (value is string strVal)
                    list.Children.Add(SecsItem.CreateAscii(strVal));
                else
                    list.Children.Add(SecsItem.CreateAscii(value?.ToString() ?? ""));
            }
            return list;
        }

        // ========== 设备常量(EC) ==========

        /// <summary>
        /// 注册设备常量。
        /// </summary>
        public void RegisterEquipmentConstant(EquipmentConstant ec)
        {
            lock (_lock)
            {
                _equipmentConstants[ec.Id] = ec;
                Log($"[GEM数据] 注册EC：ID={ec.Id}, 名称={ec.Name}, 值={ec.Value}");
            }
        }

        /// <summary>
        /// 获取设备常量值。
        /// </summary>
        public object? GetEquipmentConstantValue(uint ecId)
        {
            lock (_lock)
            {
                return _equipmentConstants.TryGetValue(ecId, out var ec) ? ec.Value : null;
            }
        }

        /// <summary>
        /// 设置设备常量值（S2F15 命令）。
        /// 返回 true 表示设置成功。
        /// </summary>
        public bool SetEquipmentConstantValue(uint ecId, object value)
        {
            lock (_lock)
            {
                if (!_equipmentConstants.TryGetValue(ecId, out var ec))
                    return false;

                // 检查范围限制
                if (ec.MinValue > double.MinValue || ec.MaxValue < double.MaxValue)
                {
                    double numVal = Convert.ToDouble(value);
                    if (numVal < ec.MinValue || numVal > ec.MaxValue)
                    {
                        Log($"[GEM数据] EC值超出范围：{numVal} (范围 {ec.MinValue}~{ec.MaxValue})");
                        return false;
                    }
                }

                ec.Value = value;
                Log($"[GEM数据] 设置EC：ID={ecId}, 新值={value}");
                return true;
            }
        }

        /// <summary>
        /// 获取所有设备常量定义。
        /// </summary>
        public List<EquipmentConstant> GetAllEquipmentConstants()
        {
            lock (_lock) { return _equipmentConstants.Values.ToList(); }
        }

        // ========== 采集事件(CE) ==========

        /// <summary>
        /// 注册采集事件。
        /// </summary>
        public void RegisterCollectionEvent(CollectionEvent ce)
        {
            lock (_lock)
            {
                _collectionEvents[ce.Id] = ce;
                Log($"[GEM数据] 注册CE：ID={ce.Id}, 名称={ce.Name}");
            }
        }

        /// <summary>
        /// 启用或禁用事件上报（S2F37 命令）。
        /// </summary>
        public void SetEventEnabled(uint ceId, bool enabled)
        {
            lock (_lock)
            {
                if (_collectionEvents.TryGetValue(ceId, out var ce))
                {
                    ce.IsEnabled = enabled;
                    Log($"[GEM数据] 事件 {ceId} {(enabled ? "启用" : "禁用")}");
                }
            }
        }

        /// <summary>
        /// 批量启用/禁用事件。
        /// ceIds 为空表示操作所有事件。
        /// </summary>
        public void SetEventsEnabled(List<uint>? ceIds, bool enabled)
        {
            lock (_lock)
            {
                if (ceIds == null || ceIds.Count == 0)
                {
                    foreach (var ce in _collectionEvents.Values)
                        ce.IsEnabled = enabled;
                }
                else
                {
                    foreach (var id in ceIds)
                        SetEventEnabled(id, enabled);
                }
            }
        }

        /// <summary>
        /// 获取所有采集事件。
        /// </summary>
        public List<CollectionEvent> GetAllCollectionEvents()
        {
            lock (_lock) { return _collectionEvents.Values.ToList(); }
        }

        // ========== 报告定义 ==========

        /// <summary>
        /// 定义报告（S2F33 命令）。
        /// 报告包含一组 SV ID，当关联的事件触发时，收集这些 SV 的值。
        /// </summary>
        public bool DefineReport(uint reportId, List<uint> svIds)
        {
            lock (_lock)
            {
                _reportDefinitions[reportId] = new ReportDefinition
                {
                    Id = reportId,
                    VariableIds = svIds
                };
                Log($"[GEM数据] 定义报告：ID={reportId}, 包含 {svIds.Count} 个变量");
                return true;
            }
        }

        /// <summary>
        /// 删除报告定义。
        /// reportId=0 表示删除所有报告。
        /// </summary>
        public void DeleteReport(uint reportId)
        {
            lock (_lock)
            {
                if (reportId == 0)
                {
                    _reportDefinitions.Clear();
                    _eventReportLinks.Clear();
                    Log("[GEM数据] 删除所有报告定义");
                }
                else
                {
                    _reportDefinitions.Remove(reportId);
                    Log($"[GEM数据] 删除报告：ID={reportId}");
                }
            }
        }

        /// <summary>
        /// 关联事件与报告（S2F35 命令）。
        /// 当事件触发时，自动收集关联报告中的变量值。
        /// </summary>
        public bool LinkEventToReports(uint ceId, List<uint> reportIds)
        {
            lock (_lock)
            {
                _eventReportLinks[ceId] = reportIds;
                Log($"[GEM数据] 事件 {ceId} 关联 {reportIds.Count} 个报告");
                return true;
            }
        }

        /// <summary>
        /// 构建 S6F11 事件报告的消息体。
        /// 根据事件关联的报告，收集所有变量值。
        /// </summary>
        public SecsItem? BuildEventReport(uint ceId)
        {
            lock (_lock)
            {
                if (!_collectionEvents.TryGetValue(ceId, out var ce) || !ce.IsEnabled)
                    return null;

                // 构建 S6F11 消息体：
                // <L [3]
                //   <U4 dataid>
                //   <U4 ceid>
                //   <L [n]     ; 报告列表
                //     <L [2]
                //       <U4 rptid>
                //       <L [m]   ; 变量值列表
                //         ...
                //       >
                //     >
                //   >
                // >
                var root = SecsItem.CreateList();
                root.Children.Add(SecsItem.CreateU4((uint)0));  // DataId
                root.Children.Add(SecsItem.CreateU4(ceId));     // CEID

                var reports = SecsItem.CreateList();
                if (_eventReportLinks.TryGetValue(ceId, out var reportIds))
                {
                    foreach (var rptId in reportIds)
                    {
                        if (_reportDefinitions.TryGetValue(rptId, out var rpt))
                        {
                            var reportItem = SecsItem.CreateList();
                            reportItem.Children.Add(SecsItem.CreateU4(rptId));

                            var values = SecsItem.CreateList();
                            foreach (var svId in rpt.VariableIds)
                            {
                                var val = GetStatusVariableValue(svId);
                                if (val is int i)
                                    values.Children.Add(SecsItem.CreateI4(i));
                                else if (val is double d)
                                    values.Children.Add(SecsItem.CreateF8(d));
                                else
                                    values.Children.Add(SecsItem.CreateAscii(
                                        val?.ToString() ?? ""));
                            }

                            reportItem.Children.Add(values);
                            reports.Children.Add(reportItem);
                        }
                    }
                }

                root.Children.Add(reports);
                return root;
            }
        }

        private void Log(string msg) => MessageLogged?.Invoke(this, msg);
    }
}
